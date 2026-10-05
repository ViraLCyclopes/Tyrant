"""
PK Animal materials: one shared node group that follows the game's animal shader (the same rules as Tyrant's 3D view):
pattern R blends colour A → B (softened by Softness) and turns on the hue/saturation/value shift, pattern G takes the
secondary colour, extra R > 0.9 is the eyes (eye colour), extra R is smoothness (roughness = 1 − R), extra G is
ambient occlusion, diffuse alpha below Cutoff is cut away. Strength 0 leaves the textures as they are. Colours are mixed
in Blender's linear space (the game mixes in gamma space), so tints are close, not identical.
"""
import os

import bpy

GROUP = "PK Animal"
MATURITY = "Maturity"

# (adult slot, infant slot, group input) — infant maps are mixed in by the Maturity value (the Growth slider sets it).
PAIRS = (("diffuse", "infantDiffuse", "Diffuse"), ("normal", "infantNormal", "Normal"),
         ("extra", "infantExtra", "Extra"), ("pattern", "infantPattern", "Pattern"))
COLOR_SLOTS = {"diffuse", "infantDiffuse"}


def _feed(tree, socket, value):
    if hasattr(value, "is_output"):
        tree.links.new(value, socket)
    else:
        socket.default_value = value


def _math(tree, op, a, b=None, clamp=False):
    node = tree.nodes.new("ShaderNodeMath")
    node.operation = op
    node.use_clamp = clamp
    _feed(tree, node.inputs[0], a)
    if b is not None:
        _feed(tree, node.inputs[1], b)
    return node.outputs[0]


def _mix(tree, factor, a, b, blend="MIX"):
    node = tree.nodes.new("ShaderNodeMix")
    node.data_type = "RGBA"
    node.blend_type = blend
    _feed(tree, node.inputs["Factor"], factor)
    _feed(tree, node.inputs["A"], a)
    _feed(tree, node.inputs["B"], b)
    return node.outputs["Result"]


def _mix_float(tree, factor, a, b):
    node = tree.nodes.new("ShaderNodeMix")
    node.data_type = "FLOAT"
    _feed(tree, node.inputs["Factor"], factor)
    _feed(tree, node.inputs["A"], a)
    _feed(tree, node.inputs["B"], b)
    return node.outputs["Result"]


def _socket(tree, name, kind, default=None, out=False):
    socket = tree.interface.new_socket(name, in_out="OUTPUT" if out else "INPUT", socket_type=kind)
    if default is not None:
        socket.default_value = default
    return socket


def ensure_group():
    tree = bpy.data.node_groups.get(GROUP)
    if tree is not None:
        return tree
    tree = bpy.data.node_groups.new(GROUP, "ShaderNodeTree")
    color, number = "NodeSocketColor", "NodeSocketFloat"
    _socket(tree, "BSDF", "NodeSocketShader", out=True)
    _socket(tree, "Diffuse", color, (0.8, 0.8, 0.8, 1.0))
    _socket(tree, "Diffuse Alpha", number, 1.0)
    _socket(tree, "Normal", color, (0.5, 0.5, 1.0, 1.0))
    _socket(tree, "Extra", color, (0.15, 1.0, 0.0, 1.0))  # roughness 0.85, no AO, no eyes
    _socket(tree, "Pattern", color, (0.0, 0.0, 0.0, 1.0))  # no recolour
    for name in ("Colour A", "Colour B", "Secondary", "Eye"):
        _socket(tree, name, color, (0.0, 0.0, 0.0, 1.0))
    for name in ("Has AB", "Has Secondary", "Has Eye", "Strength", "Hue", "Saturation", "Value"):
        _socket(tree, name, number, 0.0)
    _socket(tree, "Softness", number, 0.25)
    _socket(tree, "Cutoff", number, 0.5)

    inp = tree.nodes.new("NodeGroupInput").outputs
    out = tree.nodes.new("NodeGroupOutput")
    pattern = tree.nodes.new("ShaderNodeSeparateColor")
    tree.links.new(inp["Pattern"], pattern.inputs[0])
    extra = tree.nodes.new("ShaderNodeSeparateColor")
    tree.links.new(inp["Extra"], extra.inputs[0])
    pr, pg = pattern.outputs[0], pattern.outputs[1]
    er, eg = extra.outputs[0], extra.outputs[1]

    is_pattern = _math(tree, "GREATER_THAN", pr, 0.0)
    hsv = tree.nodes.new("ShaderNodeHueSaturation")
    tree.links.new(inp["Diffuse"], hsv.inputs["Color"])
    tree.links.new(_math(tree, "ADD", inp["Hue"], 0.5), hsv.inputs["Hue"])
    tree.links.new(_math(tree, "ADD", inp["Saturation"], 1.0), hsv.inputs["Saturation"])
    tree.links.new(_math(tree, "ADD", inp["Value"], 1.0), hsv.inputs["Value"])
    shifted = _mix(tree, is_pattern, inp["Diffuse"], hsv.outputs["Color"])

    blend = _math(tree, "DIVIDE", pr, _math(tree, "MAXIMUM", inp["Softness"], 0.01), clamp=True)
    ab = _mix(tree, blend, inp["Colour A"], inp["Colour B"])
    with_ab = _mix(tree, _math(tree, "MULTIPLY", _math(tree, "MULTIPLY", is_pattern, inp["Has AB"]), inp["Strength"]), shifted, ab)
    secondary_amount = _math(tree, "MULTIPLY", _math(tree, "MULTIPLY", _math(tree, "MULTIPLY", is_pattern, inp["Has Secondary"]), pg), inp["Strength"])
    with_secondary = _mix(tree, secondary_amount, with_ab, inp["Secondary"])
    eye_amount = _math(tree, "MULTIPLY", _math(tree, "MULTIPLY", _math(tree, "GREATER_THAN", er, 0.9), inp["Has Eye"]), inp["Strength"])
    with_eyes = _mix(tree, eye_amount, with_secondary, inp["Eye"])
    occluded = _mix(tree, 1.0, with_eyes, eg, blend="MULTIPLY")

    bsdf = tree.nodes.new("ShaderNodeBsdfPrincipled")
    tree.links.new(occluded, bsdf.inputs["Base Color"])
    tree.links.new(_math(tree, "SUBTRACT", 1.0, er), bsdf.inputs["Roughness"])
    bsdf.inputs["Metallic"].default_value = 0.0
    tree.links.new(_math(tree, "GREATER_THAN", inp["Diffuse Alpha"], _math(tree, "SUBTRACT", inp["Cutoff"], 1e-4)), bsdf.inputs["Alpha"])
    normal_map = tree.nodes.new("ShaderNodeNormalMap")
    tree.links.new(inp["Normal"], normal_map.inputs["Color"])
    tree.links.new(normal_map.outputs["Normal"], bsdf.inputs["Normal"])
    tree.links.new(bsdf.outputs["BSDF"], out.inputs["BSDF"])
    return tree


def _linear(hex_value):
    if not hex_value or len(hex_value) != 7 or not hex_value.startswith("#"):
        return None
    channels = [int(hex_value[i:i + 2], 16) / 255.0 for i in (1, 3, 5)]
    return tuple(c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4 for c in channels) + (1.0,)


def _image(tree, folder, slot, relative):
    path = os.path.join(folder, relative)
    if not os.path.isfile(path):
        return None
    node = tree.nodes.new("ShaderNodeTexImage")
    node.name = node.label = slot
    node.image = bpy.data.images.load(path, check_existing=True)
    node.image.colorspace_settings.name = "sRGB" if slot in COLOR_SLOTS else "Non-Color"
    return node


def _cut_out(material):
    for prop in ("surface_render_method", "blend_method"):
        if hasattr(material, prop):
            try:
                setattr(material, prop, "DITHERED" if prop == "surface_render_method" else "CLIP")
            except TypeError:
                pass
    material.use_backface_culling = False


def build(material, spec, folder):
    """Rebuilds the material's nodes from the project's spec (maps, colours, cutoff)."""
    if material.node_tree is None:  # use_nodes is deprecated in Blender 5 (materials always have nodes)
        material.use_nodes = True
    tree = material.node_tree
    tree.nodes.clear()
    output = tree.nodes.new("ShaderNodeOutputMaterial")
    maps = spec.get("maps") or {}
    if not spec.get("animal"):
        _build_plain(tree, output, maps, spec.get("cutoff"), folder)
        _cut_out(material)
        return
    group = tree.nodes.new("ShaderNodeGroup")
    group.node_tree = ensure_group()
    group.name = group.label = GROUP
    tree.links.new(group.outputs["BSDF"], output.inputs["Surface"])
    maturity = tree.nodes.new("ShaderNodeValue")
    maturity.name = maturity.label = MATURITY
    maturity.outputs[0].default_value = 1.0

    for adult_slot, infant_slot, target in PAIRS:
        adult = _image(tree, folder, adult_slot, maps[adult_slot]) if adult_slot in maps else None
        infant = _image(tree, folder, infant_slot, maps[infant_slot]) if infant_slot in maps else None
        if adult is None and infant is None:
            continue
        if adult is not None and infant is not None:
            tree.links.new(_mix(tree, maturity.outputs[0], infant.outputs["Color"], adult.outputs["Color"]), group.inputs[target])
            if target == "Diffuse":
                tree.links.new(_mix_float(tree, maturity.outputs[0], infant.outputs["Alpha"], adult.outputs["Alpha"]), group.inputs["Diffuse Alpha"])
        else:
            only = adult or infant
            tree.links.new(only.outputs["Color"], group.inputs[target])
            if target == "Diffuse":
                tree.links.new(only.outputs["Alpha"], group.inputs["Diffuse Alpha"])

    colors = spec.get("colors")
    if colors:
        for key, name, flag in (("a", "Colour A", "Has AB"), ("b", "Colour B", None), ("secondary", "Secondary", "Has Secondary"), ("eye", "Eye", "Has Eye")):
            value = _linear(colors.get(key))
            if value is not None:
                group.inputs[name].default_value = value
            if flag:
                group.inputs[flag].default_value = 1.0 if value is not None else 0.0
        if colors.get("b") is None and colors.get("a"):
            group.inputs["Colour B"].default_value = group.inputs["Colour A"].default_value
        group.inputs["Strength"].default_value = float(colors.get("strength") or 0.0)
        group.inputs["Softness"].default_value = max(float(colors.get("softness") or 0.25), 0.01)
        for key, name in (("hue", "Hue"), ("saturation", "Saturation"), ("value", "Value")):
            group.inputs[name].default_value = float(colors.get(key) or 0.0)
    group.inputs["Cutoff"].default_value = float(spec.get("cutoff") if spec.get("cutoff") is not None else 0.5)
    _cut_out(material)


def _build_plain(tree, output, maps, cutoff, folder):
    bsdf = tree.nodes.new("ShaderNodeBsdfPrincipled")
    bsdf.inputs["Metallic"].default_value = 0.0
    bsdf.inputs["Roughness"].default_value = 0.85
    tree.links.new(bsdf.outputs["BSDF"], output.inputs["Surface"])
    diffuse = _image(tree, folder, "diffuse", maps["diffuse"]) if "diffuse" in maps else None
    if diffuse is not None:
        tree.links.new(diffuse.outputs["Color"], bsdf.inputs["Base Color"])
        if cutoff is not None:
            tree.links.new(_math(tree, "GREATER_THAN", diffuse.outputs["Alpha"], float(cutoff) - 1e-4), bsdf.inputs["Alpha"])
    normal = _image(tree, folder, "normal", maps["normal"]) if "normal" in maps else None
    if normal is not None:
        normal_map = tree.nodes.new("ShaderNodeNormalMap")
        tree.links.new(normal.outputs["Color"], normal_map.inputs["Color"])
        tree.links.new(normal_map.outputs["Normal"], bsdf.inputs["Normal"])


def _base_name(name):
    head, dot, tail = name.rpartition(".")
    return head if dot and tail.isdigit() and len(tail) == 3 else name


def apply_all(objects, data, folder):
    """Dresses every material of the imported meshes that the project describes (once per material)."""
    specs = data.get("materials") or {}
    done = set()
    for obj in objects:
        if obj.type != "MESH":
            continue
        for slot in obj.material_slots:
            material = slot.material
            if material is None or material.name in done:
                continue
            spec = specs.get(_base_name(material.name))
            if spec is not None:
                build(material, spec, folder)
                done.add(material.name)
