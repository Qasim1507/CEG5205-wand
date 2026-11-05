"""Prepare the CC0 Innerscene torso GLB for Unity's native Mesh importer.

Requires trimesh and Pillow. Run from the project root with
``python Tools/prepare_anatomy.py`` after placing the source GLB in
ThirdPartySources/Anatomy/torso-with-removable-organs.glb.
"""

from pathlib import Path
import struct
import trimesh


ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "ThirdPartySources/Anatomy/torso-with-removable-organs.glb"
PREPARED = ROOT / "ThirdPartySources/Anatomy/Prepared"
TEXTURES = ROOT / "Assets/Surgery/Models/AnatomyTextures"


def main():
    PREPARED.mkdir(parents=True, exist_ok=True)
    TEXTURES.mkdir(parents=True, exist_ok=True)
    scene = trimesh.load(SOURCE, force="scene")
    materials = {}
    rows = []
    for index, (name, mesh) in enumerate(scene.geometry.items()):
        material = mesh.visual.material
        material_name = material.name
        if material_name not in materials:
            material_id = len(materials)
            materials[material_name] = material_id
            texture = material.baseColorTexture
            texture.thumbnail((512, 512))
            texture.convert("RGB").save(TEXTURES / f"Material_{material_id:02d}.png")
        material_id = materials[material_name]
        path = PREPARED / f"Part_{index:02d}.meshbin"
        vertices = mesh.vertices
        normals = mesh.vertex_normals
        uv = mesh.visual.uv
        faces = mesh.faces
        with path.open("wb") as stream:
            stream.write(struct.pack("<II", len(vertices), faces.size))
            for i, point in enumerate(vertices):
                normal = normals[i]
                tex = uv[i] if uv is not None else (0.0, 0.0)
                # GLB torso axes: X=lateral, Y=headward, Z=anterior.
                # Unity scene axes: X=headward, Y=up, Z=lateral.
                stream.write(struct.pack("<8f", point[1], point[2], point[0],
                                         normal[1], normal[2], normal[0],
                                         tex[0], 1.0 - tex[1]))
            stream.write(faces.astype("<u4").tobytes())
        rows.append(f"{index:02d}\t{name}\t{material_id:02d}")
    (PREPARED / "manifest.tsv").write_text("\n".join(rows) + "\n", encoding="utf-8")
    print(f"Prepared {len(rows)} meshes and {len(materials)} textures")


if __name__ == "__main__":
    main()
