"""What Send takes and what it leaves, and the problems that stop it, each said the way it is fixed in Blender."""
import bpy

from . import ik, send

MAX_UNWEIGHTED = 0.25  # the core's ModelFitter.MaxUnweightedShare


def preview(armature, scene):
    """(names Send takes, [(name, why it stays)])."""
    goes = send.sendable(armature)
    names = {o.name for o in goes}
    stays, others = [], []
    for obj in scene.objects:
        if obj.name in names:
            continue
        if obj.type == "MESH":
            stays.append((obj.name, f"not deformed by the Tyrant rig (add an Armature modifier on {armature.name})"))
        elif obj.type == "ARMATURE":
            stays.append((obj.name, "another armature"))
        else:
            others.append(obj.name)
    if len(others) <= 3:
        stays += [(name, "not a mesh") for name in others]
    else:
        stays.append((f"{len(others)} other objects", "not meshes"))
    return [o.name for o in goes], stays


def _image_into(socket):
    """The first Image Texture node feeding a socket, through any nodes in between."""
    queue = [link.from_node for link in socket.links]
    seen = set()
    while queue:
        node = queue.pop(0)
        if node in seen:
            continue
        seen.add(node)
        if node.type == "TEX_IMAGE" and node.image is not None:
            return node.image
        queue += [link.from_node for inp in node.inputs for link in inp.links]
    return None


def principled(material):
    if material is None or material.node_tree is None:
        return None
    return next((n for n in material.node_tree.nodes if n.type == "BSDF_PRINCIPLED"), None)


def base_image(material):
    """The picture a material shows: Tyrant's diffuse node, else the Principled BSDF's Base Color image."""
    if material is None or material.node_tree is None:
        return None
    node = material.node_tree.nodes.get("diffuse")
    if node is not None and node.type == "TEX_IMAGE" and node.image is not None:
        return node.image
    bsdf = principled(material)
    return _image_into(bsdf.inputs["Base Color"]) if bsdf is not None else None


def pictures(meshes):
    """Each distinct picture the meshes show → the meshes showing it (empty slots and image-less materials are skipped)."""
    found = {}
    for mesh in meshes:
        for slot in mesh.material_slots:
            image = base_image(slot.material)
            if image is not None and mesh.name not in found.setdefault(image, []):
                found[image].append(mesh.name)
    return found


def one_set_message(found):
    shown = "; ".join(f"{', '.join(meshes)}: {image.name}" for image, meshes in found.items())
    return (f"These show different pictures ({shown}). The game gives an animal one texture set: bake them onto one "
            "material and UV map first, or remove the mesh you replace.")


def _weights(mesh, bones):
    """(share of vertices with no weight on the rig's bones, groups with weight that are not bones, most used first)."""
    names = {g.index: g.name for g in mesh.vertex_groups}
    unweighted, foreign = 0, {}
    for vertex in mesh.data.vertices:
        on_rig = False
        for g in vertex.groups:
            if g.weight <= 0.0:
                continue
            name = names.get(g.group)
            if name in bones:
                on_rig = True
            elif name is not None:
                foreign[name] = foreign.get(name, 0) + 1
        unweighted += 0 if on_rig else 1
    count = len(mesh.data.vertices)
    return (unweighted / count if count else 0.0), sorted(foreign, key=lambda n: -foreign[n])


def problems(armature, data):
    """Why Send cannot go yet (empty when it can)."""
    meshes = send.sendable(armature)[1:]
    if not meshes:
        others = [o.name for o in bpy.context.scene.objects if o.type == "MESH"]
        which = f" ({', '.join(others[:3])}{', …' if len(others) > 3 else ''})" if others else ""
        return [f"No mesh is deformed by {armature.name}{which}: select your mesh, add an Armature modifier (Modifiers → "
                f"Deform → Armature) and set its Object to {armature.name}."]
    found = []
    bones = {b.name for b in armature.data.bones}
    for mesh in meshes:
        share, foreign = _weights(mesh, bones)
        if share > MAX_UNWEIGHTED:
            if foreign:
                more = ", …" if len(foreign) > 3 else ""
                found.append(f"{mesh.name} is weighted to another skeleton ({', '.join(foreign[:3])}{more}): rename those "
                             f"vertex groups to {armature.name}'s bones or reweight it in Weight Paint.")
            else:
                found.append(f"Most of {mesh.name} has no weight on {armature.name}'s bones: weight it in Weight Paint.")
    for mesh in meshes:
        have = {k.name for k in mesh.data.shape_keys.key_blocks} if mesh.data.shape_keys else set()
        for key in data.get("growthKeys") or []:
            if key not in have:
                found.append(f"{mesh.name} has no shape key '{key}': the game uses it for growth. Keep Tyrant's shape keys "
                             "(Voxel Remesh deletes them).")
    stiff = [b.name for b in armature.data.bones if not b.use_deform and not b.get(ik.BONE_TAG)]
    if stiff:
        more = ", …" if len(stiff) > 3 else ""
        found.append(f"{', '.join(stiff[:3])}{more} {'is' if len(stiff) == 1 else 'are'} not set to Deform: Send keeps only "
                     "deforming bones (Tyrant's IK bones stay out). Tick Deform in Bone Properties → Deform.")
    shown = pictures(meshes)
    if len(shown) > 1:
        found.append(one_set_message(shown))
    return found
