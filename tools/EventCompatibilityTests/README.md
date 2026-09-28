# Event ID compatibility checks

Logic tests need .NET 10, using the SDK's Newtonsoft.Json; no game files are required:

```sh
dotnet run --project tools/EventCompatibilityTests/EventCompatibilityTests.csproj
```

Runtime tests run the built UP DLL inside a cloned CrossOver player, with two generated CFG mods and a synthetic save. They exercise the actual native loader, dialogue/options, save/load, enhanced CFG injection, and the ordinary CFG provider called by F9. They do not synthesize a physical F9 keypress.

Build UP first using the root `build.py` with your local game references. Preparation requires Python with UnityPy, .NET 10, the game's Mono.Cecil, and macOS APFS clone support. The fixture must be deliberately prepared test data under a `qa` directory; never pass a player's save. Fixtures/game assemblies are not distributed here.

```sh
python3 tools/EventCompatibilityTests/prepare_qa.py --game /path/to/StudentAge --fixture /path/to/test-project/qa/fixture.save --python /path/to/python-with-UnityPy
python3 tools/EventCompatibilityTests/run_qa.py
```

The clone lives at `qa/runtime`. Preparation redirects managed saves/PlayerPrefs, blocks Steam writes, rewrites and verifies the cloned native PlayerSettings identity, disables UP automatic updates in the initial test config, and copies only the built UP plus test driver. It never launches the live installation. The runner refuses an already-running game and compares the live installation/save/settings hashes before and after. For a custom Steam bottle set `STUDENTAGE_BOTTLE_DIR`, `STUDENTAGE_BOTTLE`, and `STUDENTAGE_CROSSOVER_WINE`; the baseline guard assumes the usual StudentAge installation below that bottle.

Test modes (stop the isolated player before changing files):

- Default: no `qa/runtime/qa-mode.txt`, compatibility enabled in its isolated UP config. Full event/dialogue/save/reload checks.
- Reverse order: after a successful default run, create `qa/runtime/reverse-order.txt`, retaining its `id-map.json`; run again. Runtime IDs must remain identical while the original override winner changes.
- Disabled: remove `reverse-order.txt`, set the isolated compatibility config to false, put `disabled` in `qa-mode.txt`; verify native first-wins and ordinary reload.
- Standalone coexistence: set compatibility true, put `standalone` in `qa-mode.txt`, copy a locally built standalone `StudentAge.EventCompat.dll` into the isolated plugins folder, then re-prepare and run. UP must defer mapping and reject unsafe ordinary reload.

The preparation script keeps existing isolated config/mappings so restart tests are meaningful. Remove the copied standalone DLL after that test. Generated logs/results, game copies and fixtures are ignored by Git and never packaged in UP.
