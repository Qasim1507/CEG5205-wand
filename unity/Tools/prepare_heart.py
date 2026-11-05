"""Convert the CC0 anatomical heart GLB into Unity's native mesh input files."""

from pathlib import Path
import struct
import trimesh


ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "ThirdPartySources/Anatomy/heart-with-major-blood-vessels.glb"
PREPARED = ROOT / "ThirdPartySources/Anatomy/HeartPrepared"
TEXTURES = ROOT / "Assets/Surgery/Models/HeartTextures"


def main():
    PREPARED.mkdir(parents=True, exist_ok=True)
    TEXTURES.mkdir(parents=True, exist_ok=True)
    scene = trimesh.load(SOURCE, force="scene")
    meshes = list(scene.geometry.items())
    minimum = [min(mesh.bounds[0][axis] for _, mesh in meshes) for axis in range(3)]
    maximum = [max(mesh.bounds[1][axis] for _, mesh in meshes) for axis in range(3)]
    center = [(minimum[axis] + maximum[axis]) * 0.5 for axis in range(3)]
    materials = {}
    rows = []
    for index, (name, mesh) in enumerate(meshes):
        material = mesh.visual.material
        if material.name not in materials:
            material_id = len(materials)
            materials[material.name] = material_id
            texture = material.baseColorTexture
            texture.thumbnail((512, 512))
            texture.convert("RGB").save(TEXTURES / f"HeartMaterial_{material_id:02d}.png")
        material_id = materials[material.name]
        path = PREPARED / f"Part_{index:02d}.meshbin"
        with path.open("wb") as stream:
            stream.write(struct.pack("<II", len(mesh.vertices), mesh.faces.size))
            for i, point in enumerate(mesh.vertices):
                normal = mesh.vertex_normals[i]
                tex = mesh.visual.uv[i] if mesh.visual.uv is not None else (0.0, 0.0)
                # Rotate to make the anatomical anterior surface face the viewer.
                stream.write(struct.pack("<8f", point[2] - center[2],
                                         point[1] - center[1], center[0] - point[0],
                                         normal[2], normal[1], -normal[0],
                                         tex[0], 1.0 - tex[1]))
            stream.write(mesh.faces.astype("<u4").tobytes())
        rows.append(f"{index:02d}\t{name}\t{material_id:02d}")
    (PREPARED / "manifest.tsv").write_text("\n".join(rows) + "\n", encoding="utf-8")
    print(f"Prepared {len(rows)} anatomical heart meshes and {len(materials)} materials")


if __name__ == "__main__":
    main()
