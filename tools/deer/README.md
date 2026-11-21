# Deer model

The white-tailed deer, sculpted in Python the way `htubdawg/Claudeinoid`'s human is (see its `HANDOFF.md`): primitives as a signed
distance field with bone weights and colour regions, surface nets, region-keeping decimation to about 1,600 triangles, skin weights
(top four bones), and a split along region borders so each triangle is one flat colour (no texture).

Needs Python 3 with numpy, scipy and matplotlib. A build at the full grid takes about 90 s.

```bash
python3 deer.py 1600 0.006                                 # writes out/mesh.npz and out/deer.json
python3 preview.py out/p.png stand,graze,flag colour -90,30,150   # poses, colour | clay, yaw per column
```

- `meshtools.py`: the generic parts, copied from Claudeinoid.
- `deer.py`: the skeleton (26 bones, bind rotations zero), the primitives, regions, palettes (summer, winter) and poses.
- `preview_colour.png`, `preview_clay.png`: the current build (wireframe and the winter coat in the second).

Metres, Y up, facing +Z, hooves on y = 0, the deer's left = +X; in the game, scaled by 3.3 to units. Next: write the mesh into
`OpenWorld/src/DeerModel.cs` and a debug key to stand one in front of the car (`docs/PLAN.md`).
