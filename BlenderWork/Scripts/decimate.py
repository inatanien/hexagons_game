# Tripo が出力した高密度 GLB を、複数の目標三角形数へ減面して書き出す。
# 使い方: blender --background --factory-startup --python decimate.py -- <src.glb> <outdir>

import bpy
import sys
import os

argv = sys.argv[sys.argv.index("--") + 1:]
src = argv[0]
outdir = argv[1]
targets = [int(x) for x in argv[2].split(",")]

os.makedirs(outdir, exist_ok=True)


def load():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=src)
    return [o for o in bpy.context.scene.objects if o.type == "MESH"]


def tri_count(objs):
    n = 0
    for o in objs:
        me = o.data
        me.calc_loop_triangles()
        n += len(me.loop_triangles)
    return n


# まず元の面数を測る
meshes = load()
original = tri_count(meshes)
print("RESULT original_tris=%d meshes=%d" % (original, len(meshes)))

for target in targets:
    meshes = load()
    ratio = min(1.0, float(target) / float(original))

    for o in meshes:
        bpy.context.view_layer.objects.active = o
        o.select_set(True)
        mod = o.modifiers.new(name="Decimate", type="DECIMATE")
        mod.decimate_type = "COLLAPSE"
        mod.ratio = ratio
        # UV を保つ努力をさせる（テクスチャの歪みを抑える）
        mod.use_collapse_triangulate = True
        bpy.ops.object.modifier_apply(modifier=mod.name)
        o.select_set(False)

    got = tri_count(meshes)
    out = os.path.join(outdir, "TripoCottage_v2_dec%d.glb" % target)
    bpy.ops.export_scene.gltf(
        filepath=out,
        export_format="GLB",
        export_materials="EXPORT",
        export_texture_dir="",
    )
    size_mb = os.path.getsize(out) / 1048576.0
    print("RESULT target=%d ratio=%.6f actual_tris=%d size_mb=%.1f file=%s"
          % (target, ratio, got, size_mb, out))

print("RESULT done")
