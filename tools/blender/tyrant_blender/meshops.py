"""Mesh clean-up after import: merge the game's split vertex copies (keeping shading and shape keys) and mark UV seams."""
import bmesh

NORMALS_ATTR = "tyrant_corner_normals"


def merge_split_vertices(obj, distance=1e-5):
    """Merges vertices closer than distance; the game's shading comes back as custom normals. Returns how many went."""
    mesh = obj.data
    attr = mesh.attributes.new(NORMALS_ATTR, "FLOAT_VECTOR", "CORNER")
    attr.data.foreach_set("vector", [c for n in mesh.corner_normals for c in n.vector])
    before = len(mesh.vertices)
    bm = bmesh.new()
    bm.from_mesh(mesh)  # shape keys come along as layers
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=distance)
    bm.to_mesh(mesh)
    bm.free()
    stored = mesh.attributes[NORMALS_ATTR]
    normals = [0.0] * (len(mesh.loops) * 3)
    stored.data.foreach_get("vector", normals)
    mesh.normals_split_custom_set([normals[i:i + 3] for i in range(0, len(normals), 3)])
    mesh.attributes.remove(mesh.attributes[NORMALS_ATTR])
    return before - len(mesh.vertices)


def mark_uv_seams(obj):
    """An edge is a seam where its two faces' UVs differ at either end (an island border). Mesh borders are not seams."""
    mesh = obj.data
    if not mesh.uv_layers:
        return 0
    bm = bmesh.new()
    bm.from_mesh(mesh)
    uv = bm.loops.layers.uv.active
    marked = 0
    for edge in bm.edges:
        if len(edge.link_loops) != 2:
            continue
        a, b = edge.link_loops
        # Each loop starts at one end of the edge; compare the UVs each face gives the same vertex.
        a_uv = {a.vert.index: a[uv].uv.copy(), a.link_loop_next.vert.index: a.link_loop_next[uv].uv.copy()}
        b_uv = {b.vert.index: b[uv].uv.copy(), b.link_loop_next.vert.index: b.link_loop_next[uv].uv.copy()}
        if any((a_uv[v] - b_uv[v]).length > 1e-5 for v in a_uv if v in b_uv):
            edge.seam = True
            marked += 1
    bm.to_mesh(mesh)
    bm.free()
    return marked
