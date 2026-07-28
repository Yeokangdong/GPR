import os
import shutil

from data_agc import (
    DataAGC
)

if __name__ == "__main__":
    input_info = r".\data\input_info.txt"
    x_scale = 6
    y_scale = 1

    # file_name = "GAP_SEC02_0001.DZT"
    # file_path = os.path.join(r'.\data', file_name)

    with open(input_info, 'r') as file:
        for line in file:
            if line.startswith(("file_name:")):
                file_name = line.split(":")[1].strip()
                file_path = os.path.join('./data', file_name)
            elif line.startswith(('x scale:')):
                x_scale = float(line.split(":")[1].strip())
            elif line.startswith(('y scale:')):
                y_scale = float(line.split(":")[1].strip())

    print(f"x scale: {x_scale}, y scale: {y_scale}")
    data = DataAGC(file_path, agc_method='TG', x_scale=x_scale, y_scale=y_scale)
    print(file_name)
    image_path = data.image_agc_path

    TDA_folder_path = os.environ.get("GPR_TDA_DIR", os.path.join(".", ".gpr-runtime", "tda"))
    with open(input_info, 'r') as file:
        for line in file:
            if line.startswith("tda_dir:"):
                TDA_folder_path = line.split(":", 1)[1].strip()
    # 폴더가 이미 존재 하면 폴더 내용을
    if os.path.isdir(TDA_folder_path):
        shutil.rmtree(TDA_folder_path)
    # 폴더 생성
    os.makedirs(TDA_folder_path)

    print(f"base name: {os.path.basename(image_path)}")
    destination_path = os.path.join(TDA_folder_path, os.path.basename(image_path))

    # 파일 복사
    shutil.copy(image_path, destination_path)
    print(f"File copied to {destination_path}")
