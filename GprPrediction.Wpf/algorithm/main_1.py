import os
import shutil
import sys
from pathlib import Path

SCRIPT_DIR = Path(__file__).resolve().parent
if str(SCRIPT_DIR) not in sys.path:
    sys.path.insert(0, str(SCRIPT_DIR))

from data_agc import (
    DataAGC
)

if __name__ == "__main__":
    input_info = r".\data\input_info.txt"
    file_name = ""
    file_path = ""
    scan_range_x = 20
    scan_range_y = 3
    x_scale = 6
    y_scale = 1
    threshold = 0.5
    env_tda_dir = os.environ.get("GPR_TDA_DIR", "").strip()
    tda_dir = os.path.abspath(os.path.join(".", ".gpr-runtime", "tda"))

    with open(input_info, 'r', encoding='utf-8-sig') as file:
        for line in file:
            line = line.strip()
            if line.startswith(("file_name:")):
                file_name = line.split(":", 1)[1].strip()
                file_path = file_name if os.path.isabs(file_name) else os.path.join('./data', file_name)
            elif line.startswith("scan range x:"):
                scan_range_x = float(line.split(":", 1)[1].strip())
            elif line.startswith("scan range y:"):
                scan_range_y = float(line.split(":", 1)[1].strip())
            elif line.startswith(('x scale:')):
                x_scale = float(line.split(":", 1)[1].strip())
            elif line.startswith(('y scale:')):
                y_scale = float(line.split(":", 1)[1].strip())
            elif line.startswith("threshold:"):
                threshold = float(line.split(":", 1)[1].strip())
            elif line.startswith("tda_dir:"):
                tda_dir = line.split(":", 1)[1].strip()

    # 실행 관리자가 지정한 격리 작업 폴더를 설정 파일보다 우선 적용
    # 이전 실행의 input_info.txt가 다른 TDA 폴더를 가리키는 문제 방지
    if env_tda_dir:
        tda_dir = env_tda_dir

    if not file_name:
        raise ValueError("file_name is missing in input_info.txt")
    if not os.path.isfile(file_path):
        raise FileNotFoundError(f"scan file not found: {file_path}")

    processed_data_dir = os.path.abspath(os.path.join(".", "data", "processed_data"))
    os.makedirs(processed_data_dir, exist_ok=True)

    print(
        "input info: "
        f"file_name={file_name}, "
        f"scan_range_x={scan_range_x}, "
        f"scan_range_y={scan_range_y}, "
        f"x_scale={x_scale}, "
        f"y_scale={y_scale}, "
        f"threshold={threshold}"
    )
    data = DataAGC(file_path, agc_method='TG', x_scale=x_scale, y_scale=y_scale)
    print(file_name)
    image_path = os.path.abspath(data.image_agc_path)
    if not os.path.isfile(image_path):
        fallback_paths = [
            os.path.join(processed_data_dir, "data.jpg"),
            os.path.join(processed_data_dir, f"{Path(file_name).stem}.jpg"),
            os.path.abspath(os.path.join(".", "data", "data.jpg")),
        ]
        image_path = next((path for path in fallback_paths if os.path.isfile(path)), image_path)
    if not os.path.isfile(image_path):
        raise FileNotFoundError(f"AGC image was not created: {image_path}")

    processed_data_image_path = os.path.join(processed_data_dir, "data.jpg")
    if os.path.abspath(image_path) != os.path.abspath(processed_data_image_path):
        shutil.copy(image_path, processed_data_image_path)

    TDA_folder_path = os.path.abspath(os.path.expandvars(os.path.expanduser(tda_dir)))
    # TDA 작업 폴더는 유지하고 이번 실행에서 다시 생성하는 이미지만 정리
    # input_info.txt와 model_info.txt 같은 실행 설정 및 진단 파일 삭제 방지
    os.makedirs(TDA_folder_path, exist_ok=True)
    for artifact_name in ("data.jpg", "data.png"):
        artifact_path = os.path.join(TDA_folder_path, artifact_name)
        if os.path.isfile(artifact_path):
            os.remove(artifact_path)

    print(f"base name: {os.path.basename(processed_data_image_path)}")
    destination_path = os.path.join(TDA_folder_path, "data.jpg")

    # 파일 복사
    shutil.copy(processed_data_image_path, destination_path)
    print(f"File copied to {destination_path}")
