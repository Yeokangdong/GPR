from importlib.machinery import SourcelessFileLoader
from importlib.util import module_from_spec, spec_from_loader
from pathlib import Path


def _load_pyc_module():
    pyc_path = Path(__file__).with_suffix(".pyc")
    loader = SourcelessFileLoader("_gpr_detect_pyc", str(pyc_path))
    spec = spec_from_loader(loader.name, loader)
    if spec is None:
        raise ImportError(f"Cannot load {pyc_path}")

    module = module_from_spec(spec)
    loader.exec_module(module)
    return module


_module = _load_pyc_module()

for _name in dir(_module):
    if not _name.startswith("__"):
        globals()[_name] = getattr(_module, _name)

