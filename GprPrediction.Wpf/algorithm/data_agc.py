from importlib.machinery import SourcelessFileLoader
from importlib.util import module_from_spec, spec_from_loader
import os
from pathlib import Path


def _load_pyc_module():
    mode = os.environ.get("GPR_PREPROCESSOR_MODE", "tda").strip().lower()
    if mode not in {"normal", "tda"}:
        raise ValueError(f"Unsupported GPR_PREPROCESSOR_MODE: {mode}")

    file_name = "data_agc.pyc"
    pyc_path = Path(__file__).with_name(file_name)
    loader = SourcelessFileLoader(f"_gpr_data_agc_{mode}_pyc", str(pyc_path))
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
