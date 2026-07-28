import os
import shutil
import sys
import time
from pathlib import Path
from PIL import Image

SCRIPT_DIR = Path(__file__).resolve().parent
if str(SCRIPT_DIR) not in sys.path:
    sys.path.insert(0, str(SCRIPT_DIR))

from detect import (
    crop_image,
    yolo_detect,
    get_pred_results
)

if __name__ == "__main__":

    input_info = r".\data\input_info.txt"
    model_info = r".\model_info.txt"
    scan_range_x = 20
    scan_range_y = 3
    x_scale = 6
    y_scale = 1
    TDA = 1
    input_tda_override = None
    conf_thres = 0.5
    file_name = ""
    file_path = ""
    env_tda_dir = os.environ.get("GPR_TDA_DIR", "").strip()
    tda_dir = os.path.abspath(os.path.join(".", ".gpr-runtime", "tda"))

    def pick_existing(*candidates):
        for candidate in candidates:
            if candidate and os.path.exists(candidate):
                return candidate
        return candidates[0]

    normal_weights_file = pick_existing(
        "./Best_V1.260522.pt",
        "./Fine_tuned_V_best.pt",
        "./best.pt"
    )
    tda_weights_file = pick_existing(
        "./Best_Tda_V1.260522.pt",
        "./Fine_tuned_VI_tda_best.pt",
        normal_weights_file
    )
    model_info_exists = os.path.exists(model_info)

    with open(input_info, 'r', encoding='utf-8-sig') as file:
        for line in file:
            line = line.strip()
            if line.startswith(("file_name:")):
                file_name = line.split(":", 1)[1].strip()
                file_path = os.path.join('./data', file_name)
            elif line.startswith("scan range x:"):
                scan_range_x = float(line.split(":", 1)[1].strip())
            elif line.startswith("scan range y:"):
                scan_range_y = float(line.split(":", 1)[1].strip())
            elif line.startswith("x scale:"):
                x_scale = float(line.split(":", 1)[1].strip())
            elif line.startswith("y scale:"):
                y_scale = float(line.split(":", 1)[1].strip())
            elif line.startswith("threshold:"):
                conf_thres = float(line.split(":", 1)[1].strip())
            elif line.startswith("model_mode:"):
                input_model_mode = line.split(":", 1)[1].strip().lower()
                if input_model_mode in ("tda", "normal"):
                    input_tda_override = input_model_mode == "tda"
            elif line.startswith("use_tda:"):
                input_use_tda = line.split(":", 1)[1].strip().lower()
                input_tda_override = input_use_tda in ("1", "true", "yes", "y", "on", "tda")
            elif line.startswith("tda_dir:"):
                tda_dir = line.split(":", 1)[1].strip()

    if not file_name:
        raise ValueError("file_name is missing in input_info.txt")
    if not os.path.isfile(file_path):
        raise FileNotFoundError(f"scan file not found: {file_path}")

    tda_dir = os.path.abspath(os.path.expandvars(os.path.expanduser(tda_dir)))

    if model_info_exists:
        with open(model_info, 'r', encoding='utf-8-sig') as file:
            for line in file:
                line = line.strip()
                if line.startswith("model_mode:"):
                    model_mode = line.split(":", 1)[1].strip().lower()
                    if input_tda_override is None:
                        TDA = 1 if model_mode == "tda" else 0
                elif line.startswith("use_tda:"):
                    use_tda = line.split(":", 1)[1].strip().lower()
                    if input_tda_override is None:
                        TDA = 1 if use_tda in ("1", "true", "yes", "y", "on", "tda") else 0
                elif line.startswith("normal_weights_file:") or line.startswith("normal_model:"):
                    normal_weights_file = line.split(":", 1)[1].strip()
                elif line.startswith("tda_weights_file:") or line.startswith("tda_model:"):
                    tda_weights_file = line.split(":", 1)[1].strip()
                elif line.startswith("tda_dir:"):
                    tda_dir = os.path.abspath(os.path.expandvars(os.path.expanduser(line.split(":", 1)[1].strip())))

    if env_tda_dir:
        tda_dir = os.path.abspath(os.path.expandvars(os.path.expanduser(env_tda_dir)))

    if input_tda_override is not None:
        TDA = 1 if input_tda_override else 0

    print(
        "input info: "
        f"file_name={file_name}, "
        f"scan_range_x={scan_range_x}, "
        f"scan_range_y={scan_range_y}, "
        f"x_scale={x_scale}, "
        f"y_scale={y_scale}, "
        f"threshold={conf_thres}, "
        f"tda_dir={tda_dir}"
    )

    # print(f"threshold value: {conf_thres}")
    TDA_file_path = os.path.join(tda_dir, "data.png")

    if os.path.isfile(TDA_file_path) and TDA:
        dest_path = r".\data\processed_data"
        os.makedirs(dest_path, exist_ok=True)
        dest_path = os.path.join(dest_path, os.path.basename(TDA_file_path))
        if os.path.abspath(TDA_file_path) != os.path.abspath(dest_path):
            shutil.copy(TDA_file_path, dest_path)
        TDA = 1
    elif TDA:
        print(f"TDA mode is enabled, but TDA result was not created: {TDA_file_path}")
        TDA = 0
        print("Set TDA to zero!")
    else:
        TDA = 0
        print("Set TDA to zero!")

    if TDA:
        image_path = "./data/processed_data/data.png"

    else:
        image_path = "./data/processed_data/data.jpg"
    
    weights_file = tda_weights_file if TDA else normal_weights_file

    print(f"TDA: {TDA}")
    print(f"weights file: {weights_file}")

    if not os.path.isfile(image_path):
        raise FileNotFoundError(f"analysis image not found: {image_path}")
    if not os.path.isfile(weights_file):
        raise FileNotFoundError(f"model weights not found: {weights_file}")

    weights_path = [weights_file]

    with Image.open(image_path) as analysis_image:
        image_width, image_height = analysis_image.size

    crop_width = min(680, image_width)
    crop_height = min(680, image_height)
    crop_overlap = 170
    cropped_img_path = "./data/cropped_data"
    crop_image(image_path, cropped_img_path, crop_width, crop_height)

    image_size = [416, 416]

    if os.path.exists('./results'):
        for attempt in range(10):
            try:
                shutil.rmtree('./results')
                break
            except PermissionError:
                if attempt == 9:
                    raise
                time.sleep(0.5)

    yolo_detect(weights=weights_path, source=cropped_img_path, imgsz=image_size,
                conf_thres=conf_thres, iou_thres=0.45, max_det=5)

    prediction_results = get_pred_results(image_path, scan_range_x, scan_range_y)
    os.makedirs('./results', exist_ok=True)
    if prediction_results is not None:
        prediction_results.to_csv('./results/prediction_results.csv')
    else:
        print("No prediction results found.")
        with open('./results/prediction_results.csv', 'w', encoding='utf-8') as file:
            file.write("x1_m,x2_m,y1_m,y2_m,confidence\n")
