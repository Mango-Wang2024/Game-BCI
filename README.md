# BCI Unity Game

This project requires both the **Game-BCI** repository and the
**cVEP-Unity** repository.

## Prerequisites

Before starting, make sure you have:

-   OpenBCI installed and connected.
-   Both repositories cloned:
    -   `Game-BCI`
    -   `cVEP-Unity`
-   The correct Conda environment installed.

------------------------------------------------------------------------

# Step 1. Configure OpenBCI

1.  Open **OpenBCI GUI**.
2.  Go to **Hardware Settings**.
3.  Set the hardware to **X1**.
4.  Check the EEG signal quality.
    -   The voltage on each channel should ideally stay below **60
        µVrms**.
5.  Confirm that all EEG channels have good signal quality.
6.  Change **Networking** to **LSL**.
7.  Set the data type of **obci_eeg1** to **TimeSeriesRaw**.

------------------------------------------------------------------------

# Step 2. Start Lab Recorder

Open **Lab Recorder**.

It is recommended to save recordings in a fixed directory (for example,
the `data` folder inside `dp-cvep-1`).

Before starting a new recording, remove any old XDF files from the
directory to avoid confusion.

**Do not start recording yet.**

------------------------------------------------------------------------

# Step 3. Open the Checkpoint Terminal

Open the first terminal and run:

``` bash
tail -n 100 -f /<your_path>/dp-cvep-1/cvep_speller_env/dp-control-room/dareplane_cr_all.log | grep '\[CHECK\]'
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

``` bash
conda activate <your_env_name>
```

Go to the control-room directory:

``` bash
cd <your_path>/dp-cvep-1/cvep_speller_env/dp-control-room
```

Start the control room:

``` bash
python -m control_room.main --setup_cfg_path=configs/cvep_speller.toml
```

Replace `<your_env_name>` and `<your_path>` with your own values.

------------------------------------------------------------------------

# Step 5. Connect the Decoder

Open the **Dareplane UI** in your browser.

Click the buttons in the following order:

1.  **FIT MODEL**
2.  **UNITY ONLINE**
3.  **LOAD MODEL**
4.  **CONNECT DECODER**

Wait until the following message appears in the **CHECK** terminal:

``` text
... is pressed
```

5.  **DECODE ONLINE**

------------------------------------------------------------------------

# Step 6. Start Recording

Go back to **Lab Recorder**.

1.  Click **Update**.
2.  Select all **three streams**.
3.  Click **Start**.

The XDF recording will begin.

------------------------------------------------------------------------

# Step 7. Launch the Game

Open:

``` text
BCI_Unity_Build_Game.app
```

located in the `Builds` folder of `BCI-unity-1`.

Press **C** to start the game.

------------------------------------------------------------------------

# Step 8. Play the Game

For each trial:

1.  The game first enters the **2D view**. Choose one target.
2.  Keep your eyes fixed on the number of the selected target throughout
    the flashing period.
3.  It is recommended **not to blink** while the target is flashing.
4.  After the flashing ends, the predicted target will be highlighted
    with a **blue marker**. The game will then switch to the **3D
    view**, and the car will automatically move to the center of the
    predicted target.
5.  After each flashing period, you may blink and relax your eyes before
    the next trial begins.
6.  After all **16 trials** have been completed, an accuracy plot will
    be displayed. The predicted targets will be compared with the ground
    truth for all 16 trials.

------------------------------------------------------------------------

# Test Mode

The workflow in **Test Mode** is identical to **Game Mode**, except that
the game does not switch to the **3D view** and the car does not move.

Launch:

``` text
BCI_Unity_Build_Test.app
```

located in the `Builds` folder of `BCI-unity-1`.

In **Test Mode**:

-   The car does not move.
-   The predicted target is highlighted with a blue cue.
-   This mode allows decoder accuracy to be evaluated much faster
    because there is no waiting for the car animation.

------------------------------------------------------------------------

# Notes

-   Always verify EEG signal quality before starting.
-   Wait for the **"... is pressed"** checkpoint before clicking
    **DECODE ONLINE**.
-   Always select all **three** LSL streams before starting Lab
    Recorder.
-   Remove old XDF files before recording a new session.
-   Avoid overloading the computer or allowing it to overheat, as this
    can directly reduce decoding accuracy.
