import os
import shutil
from pathlib import Path

from data_agc import DataAGC


def read_input_info(path):
    values = {}
    with open(path, "r", encoding="utf-8-sig") as file:
        for raw_line in file:
            key, separator, value = raw_line.partition(":")
            if separator:
                values[key.strip().lower()] = value.strip()
    return values


if __name__ == "__main__":
    algorithm_dir = Path(__file__).resolve().parent
    data_dir = algorithm_dir / "data"
    values = read_input_info(data_dir / "input_info.txt")

    file_name = values.get("file_name", "")
    if not file_name:
        raise ValueError("file_name is missing from data/input_info.txt")

    file_path = Path(file_name)
    if not file_path.is_absolute():
        file_path = data_dir / file_path
    if not file_path.exists():
        raise FileNotFoundError(f"Scan file not found: {file_path}")

    x_scale = float(values.get("x scale", "4"))
    y_scale = float(values.get("y scale", "1"))
    print(f"preprocessor mode: {os.environ.get('GPR_PREPROCESSOR_MODE', 'normal')}")
    print(f"x scale: {x_scale}, y scale: {y_scale}")

    data = DataAGC(
        str(file_path),
        agc_window_size=100,
        agc_method="TG",
        x_scale=x_scale,
        y_scale=y_scale,
    )

    source_image = Path(data.image_agc_path)
    if not source_image.exists():
        raise FileNotFoundError(f"AGC output was not created: {source_image}")

    output_dir = data_dir / "processed_data"
    output_dir.mkdir(parents=True, exist_ok=True)
    output_image = output_dir / "data.jpg"
    if source_image.resolve() != output_image.resolve():
        shutil.copy2(source_image, output_image)

    print(f"AGC output: {output_image}")
