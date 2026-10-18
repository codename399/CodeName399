# Prediction Model

The live equity prediction model is loaded from the file configured by `TradingConfiguration.Prediction.ModelFileName`.

Default:
`Data/PredictionModels/equity-prediction-model.json`

The model is intentionally not seeded with arbitrary weights. If the model is missing or incompatible, BUY prediction is blocked safely.

Train/load it through the authenticated endpoint:
`POST /trading/prediction/train`

With no body, the endpoint uses `TradingConfiguration.Prediction.TrainingDataPath`. The path may be a directory containing ZIP archives or a specific ZIP file.
