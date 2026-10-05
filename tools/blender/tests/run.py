# Runs the add-on's tests inside Blender: blender -b --factory-startup --python tools/blender/tests/run.py -- <project.json> [pattern]
import os
import sys
import traceback
import unittest

here = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(here))  # tools/blender → import tyrant_blender
sys.path.insert(0, here)
args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
os.environ["TYRANT_TEST_PROJECT"] = args[0] if args else ""
pattern = args[1] if len(args) > 1 else "test_*.py"

try:
    import tyrant_blender

    tyrant_blender.register()
    suite = unittest.defaultTestLoader.discover(here, pattern=pattern)
    result = unittest.TextTestRunner(verbosity=2).run(suite)
    ok = result.wasSuccessful() and result.testsRun > 0
    print(f"TYRANT-TESTS ran {result.testsRun}")
except Exception:
    traceback.print_exc()
    ok = False
sys.stdout.flush()
os._exit(0 if ok else 1)  # sys.exit inside Blender's --python is swallowed; the exit code must reach Tyrant's test
