GPR algorithm runtime notes

Execution order
1. main_1.py
2. tda.jl
3. main_2.py

main_1.py
- Reads ./data/input_info.txt.
- Uses file_name, scan range, scale, and confidence threshold values written by the WPF app.
- Converts the selected scan file into ./data/processed_data/data.jpg.
- Copies data.jpg into the run-specific TDA folder.

tda.jl
- Reads model_info.txt and the run-specific TDA folder.
- Uses tda_threshold for TDA preprocessing.
- Reads data.jpg from the TDA folder.
- Writes data.png into the TDA folder when TDA succeeds.

main_2.py
- Reads model_info.txt.
- If data.png exists in the TDA folder, uses the TDA model.
- If data.png does not exist, falls back to data.jpg and the normal model.
- Writes ./results/prediction_results.csv.

Important paths
- The app sets GPR_TDA_DIR for every run.
- input_info.txt and model_info.txt also contain tda_dir.
- A fixed global TDA folder is not used by the packaged app.

Models
- Best_Tda_V1.260522.pt: used when TDA output exists.
- Best_V1.260522.pt: used when TDA output does not exist.

Notes
- detect.pyc and data_agc.pyc target CPython 3.11.
- The packaged runtime must keep Python 3.11.x.
- The app deletes transient run folders before each analysis so stale results do not get reused.
