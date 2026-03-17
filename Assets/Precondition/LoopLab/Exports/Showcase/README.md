# LoopLab Showcase Exports

This directory stores the public-facing LoopLab showcase assets checked into the repository.

Regenerate the full set from Unity with either:

- `Precondition/LoopLab/Export Showcase Assets`
- `"/Applications/Unity/Hub/Editor/6000.3.7f1/Unity.app/Contents/MacOS/Unity" -batchmode -projectPath "$PWD" -executeMethod Precondition.LoopLab.Editor.Export.LoopLabShowcaseExporter.RunBatchExport -quit -logFile "$PWD/log/looplab-showcase-export.log"`

For unattended visual validation that also verifies the generated outputs, run:

- `"/Applications/Unity/Hub/Editor/6000.3.7f1/Unity.app/Contents/MacOS/Unity" -batchmode -projectPath "$PWD" -executeMethod Precondition.LoopLab.Editor.Export.LoopLabShowcaseExporterBatchValidation.Run -quit -logFile "$PWD/log/looplab-visual-validation.log"`

Use the repo's standard `-nographics` batchmode command for compile/open validation only. Showcase media generation and boundary-capture validation now fail fast under the Null graphics device instead of writing flat placeholder renders.

Expected outputs:

- `looplab-landscape-showcase.gif`
- `looplab-landscape-thumbnail.png`
- `looplab-fluid-showcase.gif`
- `looplab-fluid-thumbnail.png`
- `looplab-geometric-showcase.gif`
- `looplab-geometric-thumbnail.png`
- `looplab-showcase-comparison-sheet.png`

Curated export settings:

- `Landscape`: `seed 142857`, `24 FPS`, `3s`, `512px`
- `Fluid`: `seed 271828`, `24 FPS`, `3s`, `512px`
- `Geometric`: `seed 314159`, `24 FPS`, `3s`, `512px`
