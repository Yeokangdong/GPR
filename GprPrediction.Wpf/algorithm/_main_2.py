import os
import shutil

from detect import (
    crop_image,
    yolo_detect,
    get_pred_results
)

if __name__ == "__main__":

    input_info = r".\data\input_info.txt"
    # scan_range_x = 20
    # scan_range_y = 2
    TDA = 0
    with open(input_info, 'r') as file:
        for line in file:
            if line.startswith(("file_name:")):
                file_name = line.split(":")[1].strip()
                file_path = os.path.join('./data', file_name)
            elif line.startswith("scan range x:"):
                scan_range_x = float(line.split(":")[1].strip())
            elif line.startswith("scan range y:"):
                scan_range_y = float(line.split(":")[1].strip())
            elif line.startswith("TDA:"):
                TDA = int(line.split(":")[1].strip())
            elif line.startswith("tda_dir:"):
                tda_dir = line.split(":", 1)[1].strip()

    tda_dir = locals().get("tda_dir", os.environ.get("GPR_TDA_DIR", os.path.join(".", ".gpr-runtime", "tda")))
    TDA_file_path = os.path.join(tda_dir, "data.png")

    if os.path.isfile(TDA_file_path):
        dest_path = r".\data\processed_data"
        dest_path = os.path.join(dest_path, os.path.basename(TDA_file_path))
        shutil.copy(TDA_file_path, dest_path)
    else:
        print("Can not find tda processed data file!")
        print("Set TDA to zero!")
        TDA = 0


    if TDA:
        image_path = "./data/processed_data/data.png"

    else:
        image_path = "./data/processed_data/data.jpg"

    conf_thres = 0.5
    weights_file = "./Fine_tuned_V_best.pt"

    weights_path = [weights_file]

    crop_width = 680
    crop_height = 680
    crop_overlap = 170
    cropped_img_path = "./data/cropped_data"
    crop_image(image_path, cropped_img_path, crop_width, crop_height)

    image_size = [416, 416]

    if os.path.exists('./results'):
        # 폴더가 이미 존재하면 폴더 내용을 지움
        shutil.rmtree('./results')

    yolo_detect(weights=weights_path, source=cropped_img_path, imgsz=image_size,
                conf_thres=conf_thres, iou_thres=0.45, max_det=5)

    prediction_results = get_pred_results(image_path, scan_range_x, scan_range_y)
    if prediction_results is not None:
        prediction_results.to_csv('./results/prediction_results.csv')
    else:
        print("No prediction results found.")
