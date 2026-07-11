# BCI Unity Game
## Demo
Built a real-time cVEP EEG-based system that identifies the visual target attended by a user and transmits the decoded result to a Unity simulation environment, where a vehicle autonomously moves to the corresponding target.

https://github.com/user-attachments/assets/c96b3f29-19fc-4997-954b-4203ca7358d5



This project uses Unity as the game frontend and a Python cVEP pipeline as the decoder/speller backend.

The full program uses:

- `BCI_Unity_Build_Game.app`: Unity game frontend for Game Mode.
- `BCI_Unity_Build_Test.app`: Unity game frontend for Test Mode.
- `cVEP-Unity`: Python cVEP decoder, speller, configuration files, and data pipeline.

The Unity source code is available in `Game-BCI`, but it is not required if you already have the `.app` files.

## Required Files

Make sure you have the Unity app files:

```text
BCI_Unity_Build_Game.app
BCI_Unity_Build_Test.app
```

Clone the Python backend:

```bash
git clone https://github.com/Mango-Wang2024/cVEP-Unity.git
```

After setup, the folder/files should look like this:

```text
your_workspace/
├── BCI_Unity_Build_Game.app
├── BCI_Unity_Build_Test.app
└── cVEP-Unity/
```

## Optional: Unity Source Code
> **Important:** If you already have the `.app` files, you do not need this section. You only need this section if you want to inspect, modify, or rebuild the Unity project.

If you want to inspect or modify the Unity project, clone the Unity source code with submodules:

```bash
git clone --recurse-submodules https://github.com/Mango-Wang2024/Game-BCI.git
```

If you already cloned `Game-BCI` without submodules, run:

```bash
cd Game-BCI
git submodule update --init --recursive
```

This downloads the `MobileUIButtons` submodule.

## Prerequisites

Before starting, make sure you have:

- OpenBCI installed and connected.
- The Unity app files:
  - `BCI_Unity_Build_Game.app`
  - `BCI_Unity_Build_Test.app`
- The Python backend repository cloned:
  - `cVEP-Unity`
- The correct Conda environment installed.

------------------------------------------------------------------------

# Step 1. Configure OpenBCI

1. Open **OpenBCI GUI**.
2. Go to **Hardware Settings**.
3. Set the hardware to **X1**.
4. Check the EEG signal quality.
   - The voltage on each channel should ideally stay below **60 µVrms**.
5. Confirm that all EEG channels have good signal quality.
6. Change **Networking** to **LSL**.
7. Set the data type of **obci_eeg1** to **TimeSeriesRaw**.

------------------------------------------------------------------------

# Step 2. Start Lab Recorder

Open **Lab Recorder**.

It is recommended to save recordings in a fixed directory, for example
the `data` folder inside `cVEP-Unity`.

Before starting a new recording, remove any old XDF files from the
directory to avoid confusion.

**Do not start recording yet.**

------------------------------------------------------------------------

# Step 3. Open the Checkpoint Terminal

Open the first terminal and run:

```bash
tail -n 100 -f /<your_path>/cVEP-Unity/cvep_speller_env/dp-control-room/dareplane_cr_all.log | grep '\[CHECK\]'
```

Replace `<your_path>` with the actual path on your computer.

This terminal displays checkpoint messages to verify that both the UDP
communication and the LSL stream are working correctly.

> **Note:** During **Step 5**, every button click generates checkpoint
> messages. You can ignore most of them. The only checkpoint you need to
> pay attention to is the one that appears after clicking **CONNECT
> DECODER**.

------------------------------------------------------------------------

# Step 4. Start the Control Room

Open a second terminal.

Activate your Conda environment:

```bash
conda activate <your_env_name>
```

Go to the control-room directory:

```bash
cd <your_path>/cVEP-Unity/cvep_speller_env/dp-control-room
```

Start the control room:

```bash
python -m control_room.main --setup_cfg_path=configs/cvep_speller.toml
```

Replace `<your_env_name>` and `<your_path>` with your own values.

------------------------------------------------------------------------

# Step 5. Connect the Decoder

Launch the Game first, but do not start it now.
Open the provided app file:

```text
BCI_Unity_Build_Game.app
```

Open the **Dareplane UI** in your browser.

Click the buttons in the following order:

1. **FIT MODEL**
2. **UNITY ONLINE**
3. **LOAD MODEL**
4. **CONNECT DECODER**

Wait until the following message appears in the **CHECK** terminal:

```text
... is pressed
```

5. **DECODE ONLINE**

------------------------------------------------------------------------

# (Optional) Step 6. Start Recording

> **Important:** This step is only needed if you want to save an XDF file for later EEG signal analysis.

Go back to **Lab Recorder**.

1. Click **Update**.
2. Select all **three streams**.
3. Click **Start**.

The XDF recording will begin.

------------------------------------------------------------------------

# Step 7. Go back to the Game

Press **C** to start the game.

------------------------------------------------------------------------

# Step 8. Play the Game

For each trial:

1. The game first enters the **2D view**. Choose one target.
2. Keep your eyes fixed on the number of the selected target throughout
   the flashing period.
3. After the flashing ends, the predicted target will be highlighted
   with a **blue marker**. The game will then switch to the **3D view**,
   and the car will automatically move to the center of the predicted target.
4. After each flashing period, you may blink and relax your eyes before
   the next trial begins.
5. After all **16 trials** have been completed, an accuracy plot will
   be displayed. The predicted targets will be compared with the ground
   truth for all 16 trials.

------------------------------------------------------------------------

# Test Mode

The workflow in **Test Mode** is identical to **Game Mode**, except that
the game does not switch to the **3D view** and the car does not move.

Open the provided test app file:

```text
BCI_Unity_Build_Test.app
```

In **Test Mode**:

- The car does not move.
- The predicted target is highlighted with a blue cue.
- This mode allows decoder accuracy to be evaluated much faster
  because there is no waiting for the car animation.

------------------------------------------------------------------------

# Notes

- Always verify EEG signal quality before starting.
- Wait for the **"... is pressed"** checkpoint before clicking
  **DECODE ONLINE**.
- Always select all **three** LSL streams before starting Lab Recorder.
- Remove old XDF files before recording a new session.
- Avoid overloading the computer or allowing it to overheat, as this
  can directly reduce decoding accuracy.
