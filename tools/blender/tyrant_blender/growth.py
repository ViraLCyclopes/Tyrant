"""
The Growth slider (0 baby – 1 adult), as AnimalGrowthManager grows an animal: the growth shape keys from the
blend-shape curve, the infant → adult maps from the skin curve, and the bone proportions (GrowthData) — those relative to
the adult, so Growth 1 is exactly the rest pose that is edited and sent. The curves come sampled from Tyrant; without a
data dump they are straight lines and bones keep their proportions. Growth sets only the location and scale the game's
growth data owns, never rotations, so poses and IK survive it. It also shows the animal's real size by scaling the armature
object as the game does, S(m, s, 1) = curve(m) · (1 + (s − 1) · fade(m)); that scale is display only and never sent.
"""
import json
import os

from mathutils import Matrix, Quaternion, Vector

from . import project

_cache = {}
BASE = "tyrant_growth_base"  # armature: each growth bone's location and scale in the prefab stance it opened in
SCALE_BASE = "tyrant_scale_base"  # armature: the object's own scale, without the size shown
SCALE_SHOWN = "tyrant_scale_shown"  # the size last shown on top of it
EDITS = "tyrant_size_edits"  # the Size panel's changes not sent yet (JSON: adult, baby, male, female)
SIZE_KEYS = ("adult", "baby", "male", "female")
SHOWN_MIN, SHOWN_MAX = 0.01, 100.0  # Tyrant's own bounds (SizeLimit.PastBounds): the view shows any size within these; past them the game caps it
# The game's genetic size ranges when the project has none (SpeciesSizes.DefaultIncubation and DefaultBreeding).
GAME_INCUBATION, GAME_BREEDING = (0.925, 1.075), (0.85, 1.15)


def sample(table, t):
    """The table's value at t (0–1), linear between its evenly spaced samples; no table = t itself."""
    t = min(max(t, 0.0), 1.0)
    if not table:
        return t
    x = t * (len(table) - 1)
    i = min(int(x), len(table) - 2)
    return table[i] + (table[i + 1] - table[i]) * (x - i)


def _remap(a, b, c, d, x):
    return c + (x - a) * (d - c) / (b - a)


def blend_maturity(table, value, clamp):
    """The blend-shape maturity at this growth: past 0.5 the game squeezes the rest of growth into 0.5..clamp (per sex)."""
    if value < 0.5:
        return sample(table, value)
    return sample(table, _remap(0.5, 1.0, 0.5, clamp, value))


def sex_of(armature):
    return "female" if getattr(armature, "tyrant_sex", "MALE") == "FEMALE" else "male"


def shape_values(m, relative):
    """The game's weights for its shape keys 0 and 1 at blend maturity m, as 0–1 (the game's weights stay within 0–100)."""
    key1 = _remap(0.0, 0.5, 100.0, 0.0, m)
    if relative:
        key0 = _remap(0.5, 1.0, 100.0, 0.0, m) if m > 0.5 else _remap(0.0, 0.5, 0.0, 100.0, m)
    else:
        key0 = _remap(0.5, 1.0, 100.0, 0.0, m)
    clamp = lambda w: round(min(max(w, 0.0), 100.0) / 100.0, 6)  # noqa: E731
    return clamp(key0), clamp(key1)


def forget(path):
    _cache.pop(path, None)


def _project(armature):
    path = armature.get(project.TAG)
    if not path or not project.is_local(path) or not os.path.isfile(path):
        return None
    stamp = os.path.getmtime(path)
    cached = _cache.get(path)
    if cached is None or cached[0] != stamp:
        cached = _cache[path] = (stamp, project.load(path))
    return cached[1]


def deformed_meshes(armature, scene_objects):
    return [o for o in scene_objects if o.type == "MESH" and (
        o.parent == armature or any(m.type == "ARMATURE" and m.object == armature for m in o.modifiers))]


def _stage(bone, value):
    """[px,py,pz,sx,sy,sz] at this growth, lerped baby → adolescent → adult as ApplyLoadedBoneProportions."""
    stages = (bone["baby"], bone["adolescent"], bone["adult"])
    first = 0 if value < 0.5 else 1
    t = value * 2 if value < 0.5 else value * 2 - 1
    return [a + (b - a) * t for a, b in zip(stages[first], stages[first + 1])]


def _basis(rest, bone, value, pose_bone, offsets=None, sigma=None):
    """The pose basis (location, scale) for this growth, relative to the game's adult stage. The game moves the bone in its
    parent's space: that move is turned into the bone's own rest frame (its bind pose, which can differ from the prefab's
    pose the stages are written against), so it lands where the game puts it. A rig edit on the bone turns and scales that
    move as the game composes it (rotate × (scale ⊙ move)); the parent's scale Blender's rest lost is put back."""
    target = _stage(bone, value)
    adult = bone["adult"]
    location, scale = Vector((0.0, 0.0, 0.0)), Vector((1.0, 1.0, 1.0))
    if bone.get("translation"):
        move = Vector(target[0:3]) - Vector(adult[0:3])
        if offsets and bone["name"] in offsets:
            _m, turn, grow = offsets[bone["name"]]
            move = turn @ Vector((grow.x * move.x, grow.y * move.y, grow.z * move.z))
        b = pose_bone.bone
        if sigma and b.parent is not None and b.parent.name in sigma:
            lost = sigma[b.parent.name]
            move = Vector((lost.x * move.x, lost.y * move.y, lost.z * move.z))
        if b.parent is not None:  # Blender keeps each bone's axes as the node's: the parent bone's frame is the parent's space
            frame = (b.parent.matrix_local.inverted() @ b.matrix_local).to_3x3().normalized()
        else:
            frame = Quaternion((rest["rotation"][3], rest["rotation"][0], rest["rotation"][1], rest["rotation"][2])).to_matrix()
        location = frame.inverted() @ move
    if bone.get("scale"):
        scale = Vector([t / a if abs(a) > 1e-8 else 1.0 for t, a in zip(target[3:6], adult[3:6])])
    return Matrix.LocRotScale(location, None, scale)


def remember_base(armature, data):
    """Called on import, in the prefab's stance: Growth adds its moves to each growth bone's own location and scale there."""
    bones = ((data or {}).get("growth") or {}).get("bones") or []
    found = [armature.pose.bones.get(b["name"]) for b in bones]
    armature[BASE] = json.dumps({p.name: list(p.location) + list(p.scale) for p in found if p is not None})


def _bases(armature):
    try:
        return {n: (Vector(v[0:3]), Vector(v[3:6])) for n, v in json.loads(armature.get(BASE) or "{}").items()}
    except (ValueError, TypeError, IndexError):
        return {}


def fade(m):
    """The game's maturity fade (GlobalAnimalParameters): how much of the sex size shows, 0.2 at hatching to 1 when adult."""
    m = min(max(m, 0.0), 1.0)
    return 0.2 + 0.8 * (1.0 - (1.0 - m) ** 2)


def evaluate(keys, t):
    """The curve at t, linear between its [time, value] keys (SizeMath.Evaluate). Tyrant sends the game's curve sampled as the
    game reads it (Hermite, 33 times over 0–1), so the lines keep its shape."""
    if not keys:
        return 0.0
    if t <= keys[0][0]:
        return keys[0][1]
    for (t0, v0), (t1, v1) in zip(keys, keys[1:]):
        if t <= t1:
            return v1 if t1 <= t0 else v0 + (v1 - v0) * (t - t0) / (t1 - t0)
    return keys[-1][1]


def rebuild(original, adult, baby):
    """SizeMath.Rebuild: the curve for these sizes, 17 keys over 0–1 of adult · shape(m) / (1 + (adult − 1) · fade(m)) and the
    original keys past 1 mapped by shape, which rescales the original so that its start is baby × its end."""
    c0, c1 = evaluate(original, 0.0), evaluate(original, 1.0)
    share0 = 1.0 if c1 == 0 else c0 / c1
    a, b = 0.0, 1.0
    if 1 - share0 >= 1e-4:
        b = c1 * (1 - (share0 if baby is None else baby)) / (c1 - c0)
        a = c1 - b * c1
    keys = [[i / 16, adult * (a + b * evaluate(original, i / 16)) / (1 + (adult - 1) * fade(i / 16))] for i in range(17)]
    return keys + [[t, a + b * v] for t, v in sorted(original) if t > 1]


def size_edits(armature):
    """The Size panel's changes not sent yet ({key: value})."""
    try:
        edits = json.loads(armature.get(EDITS) or "{}")
    except (ValueError, TypeError):
        return {}
    return {k: float(v) for k, v in edits.items() if k in SIZE_KEYS and isinstance(v, (int, float))} if isinstance(edits, dict) else {}


def sizes_of(armature, data):
    """The species' sizes shown ({key: value or None = unchanged}): the destination's, with the panel's changes on top when
    this project may change them; None without size data."""
    sizes = (data or {}).get("sizes")
    if not sizes:
        return None
    shown = {k: sizes.get(k) for k in SIZE_KEYS}
    if data.get("sizesEditable"):
        shown.update(size_edits(armature))
    return shown


def changed_sizes(armature, data, editable=None):
    """What Send writes to the species: only the keys edited here since the last Open or Send ({key: value}), which Tyrant
    merges onto the mod's current sizes (the app may have changed the others meanwhile). None when this project may not
    change them (editable: whether the destination chosen now may, else the project's) or nothing changed."""
    if not data or not (data.get("sizesEditable") if editable is None else editable):
        return None
    sizes = data.get("sizes") or {}
    changed = {k: v for k, v in size_edits(armature).items() if sizes.get(k) is None or abs(sizes[k] - v) >= 1e-9}
    return changed or None


def baby_share(data):
    """The newborn's size as a share of the adult's in the game's own curve (1 without one)."""
    original = ((data or {}).get("growth") or {}).get("originalScale")
    end = evaluate(original, 1.0) if original else 0.0
    return evaluate(original, 0.0) / end if end else 1.0


def sex_sizes(armature, data):
    """(male, female): each sex's size in game, the skin's own times the adult and sex sizes shown."""
    sizes = sizes_of(armature, data)
    if sizes is None:
        sexes = (data or {}).get("sexes") or {}
        return tuple(float((sexes.get(sex) or {}).get("size", 1.0)) for sex in ("male", "female"))
    own = data["sizes"]
    return tuple(own[f"{sex}Original"] * (sizes["adult"] or 1.0) * (sizes[sex] or 1.0) for sex in ("male", "female"))


def _shown_curve(armature, data):
    """The scale curve of the sizes shown (rebuilt for the panel's changes as Tyrant does for the game); None without one."""
    growth = (data or {}).get("growth") or {}
    curve = growth.get("scale")
    if not curve:
        return None
    original = growth.get("originalScale")
    sizes = sizes_of(armature, data)
    if original and sizes and size_edits(armature) and data.get("sizesEditable"):
        adult, baby = sizes["adult"] or 1.0, sizes["baby"]
        curve = original if adult == 1.0 and baby is None else rebuild(original, adult, baby)
    return curve


def _scale(curve, s, maturity):
    """S(m, s, 1): the game's final scale at this maturity for sex size s, before its limit."""
    return evaluate(curve, maturity) * (1 + (s - 1) * fade(maturity))


def size_factor(armature, data, maturity):
    """The animal's size at this maturity (the Growth slider's value) as the game scales it, S(m, s, 1) for the sex shown;
    None without a scale curve. It shows the size set, within Tyrant's own bounds only: past them the game caps it, which the
    Size box's note says (limit_note)."""
    curve = _shown_curve(armature, data)
    if curve is None:
        return None
    male, female = sex_sizes(armature, data)
    s = female if sex_of(armature) == "female" else male
    return min(max(_scale(curve, s, maturity), SHOWN_MIN), SHOWN_MAX)


def genetic_sizes(data):
    """The genetic sizes the extremes count: the ends of the species' ranges in effect (SizePlan.InEffect), random eggs',
    its Nursery slider's (its own, else the random eggs') and its breeding range (its own, else the game's)."""
    ranges = (data or {}).get("geneticRanges") or {}
    incubation = tuple(ranges.get("incubation") or GAME_INCUBATION)
    nursery = tuple(ranges.get("nursery") or incubation)
    breeding = tuple(ranges.get("breeding") or GAME_BREEDING)
    return [g for r in (incubation, nursery, breeding) for g in r]


def extremes(armature, data):
    """(min, max): the smallest and largest scale the species reaches with the sizes shown, as Check and Send to Tyrant count
    it (SpeciesSizes.Extremes): its curve rebuilt for them, every skin's sexes and the genetic ranges in effect, over maturity
    0-1 (101 samples); None without a scale curve."""
    growth = (data or {}).get("growth") or {}
    sizes = sizes_of(armature, data)
    original = growth.get("originalScale")
    if original and sizes is not None:
        curve = rebuild(original, sizes["adult"] or 1.0, sizes["baby"])
    else:
        curve = _shown_curve(armature, data)
    if curve is None:
        return None
    if sizes is None:
        sexes = sex_sizes(armature, data)
    else:
        own = data["sizes"]
        skins = data.get("sizeSkins") or [[own["maleOriginal"], own["femaleOriginal"]]]
        adult = sizes["adult"] or 1.0
        sexes = [size * adult * (sizes[sex] or 1.0) for male, female in skins for sex, size in (("male", male), ("female", female))]
    genetics = genetic_sizes(data)
    scales = [evaluate(curve, m) * (1 + (s * g - 1) * fade(m)) for m in (i / 100 for i in range(101)) for s in sexes for g in genetics]
    return min(scales), max(scales)


def limit_note(armature, data):
    """The Size box's note when the species' sizes pass Tyrant's own bounds (over 100 or under 0.01), where the game caps them:
    within them its size limit follows the sizes (automatic, SizeLimit.Fitting), so there is nothing to say. None within them."""
    found = extremes(armature, data)
    if found is None:
        return None
    if found[1] > SHOWN_MAX:
        return f"In game this is capped at {SHOWN_MAX:g}×."
    if found[0] < SHOWN_MIN:
        return f"In game this is capped at {SHOWN_MIN:g}×."
    return None


def own_scale(armature):
    """The armature object's scale without the size shown; a scale you gave the object since counts as your own."""
    shown = float(armature.get(SCALE_SHOWN, 1.0)) or 1.0
    current = Vector(armature.scale)
    base = armature.get(SCALE_BASE)
    if base is not None and len(base) == 3 and (Vector(base) * shown - current).length <= 1e-5 * max(current.length, 1.0):
        return Vector(base)
    return current / shown


def _show_size(armature, factor):
    """Scales the armature object by the size shown (None: none) on top of its own scale."""
    if factor is None and SCALE_BASE not in armature:
        return
    base = own_scale(armature)
    factor = 1.0 if factor is None else factor
    armature[SCALE_BASE] = list(base)
    armature[SCALE_SHOWN] = factor
    armature.scale = base * factor


def hide_size(armature):
    """Puts the object's own scale back, for the export: the size shown is never sent (Growth shows it again after)."""
    _show_size(armature, None)


def set_growth(armature, value, scene_objects=None):
    """Shows the animal at this growth (0 baby – 1 adult)."""
    import bpy

    data = _project(armature)
    growth = (data or {}).get("growth") or {}
    sexes = (data or {}).get("sexes") or {}
    clamp = float((sexes.get(sex_of(armature)) or {}).get("growthClamp", 1.0))
    m = blend_maturity(growth.get("blend"), value, clamp)
    key0, key1 = shape_values(m, growth.get("relative", True))
    maturity = sample(growth.get("skin"), value)
    # The size follows the maturity itself (AnimalNative.CurrentScale), not the skin curve, which only blends the textures.
    _show_size(armature, size_factor(armature, data, min(max(value, 0.0), 1.0)))
    meshes = deformed_meshes(armature, scene_objects if scene_objects is not None else bpy.context.scene.objects)
    for mesh in meshes:
        keys = mesh.data.shape_keys.key_blocks if mesh.data.shape_keys else []
        for index, weight in ((1, key0), (2, key1)):  # block 0 is the Basis; 1 and 2 are the game's keys 0 and 1
            if len(keys) > index:
                keys[index].value = weight
        for slot in mesh.material_slots:
            node = slot.material.node_tree.nodes.get("Maturity") if slot.material and slot.material.node_tree else None
            if node is not None:
                node.outputs[0].default_value = maturity

    from . import ik  # late: ik uses growth too

    from . import rig  # late: rig uses growth too

    rest = {r["name"]: r for r in (data or {}).get("rest", [])}
    bases = _bases(armature)
    offsets, sigma = rig.offsets(armature), rig._sigma(armature)
    channels = {}
    for bone in growth.get("bones") or []:
        pose_bone = armature.pose.bones.get(bone["name"])
        if pose_bone is None or bone["name"] not in rest:
            continue
        change = Matrix.Identity(4) if value >= 1.0 else _basis(rest[bone["name"]], bone, value, pose_bone, offsets, sigma)
        # Only the channels the game's growth owns, on top of the stance the model opened in: rotations are yours.
        base_location, base_scale = bases.get(bone["name"], (Vector((0.0, 0.0, 0.0)), Vector((1.0, 1.0, 1.0))))
        location = base_location + change.translation
        scale = Vector([b * c for b, c in zip(base_scale, change.to_scale())])
        if bone.get("translation"):
            pose_bone.location = location
        if bone.get("scale"):
            pose_bone.scale = scale
        channels[bone["name"]] = (location if bone.get("translation") else None, scale if bone.get("scale") else None)
    armature["tyrant_growth_shown"] = value
    ik.follow_growth(armature, channels)
