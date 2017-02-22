using System;
using System.IO.Ports;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using SharpDX.XInput;
using APOPHIS.GroundStation.Input.Xbox;
using APOPHIS.GroundStation.Packet.Data;
using APOPHIS.GroundStation.Helpers;

namespace APOPHIS.GroundStation.GUI {
  /// <summary>
  /// Interaction logic for MainWindow.xaml
  /// </summary>
  public partial class MainWindow : Window, IDisposable {
    // Declare the serial com port
    SerialPort _COMPort = new SerialPort();
    
    //
    // Set up a controller.
    XboxController _controller = new XboxController();
    
    //
    // Setup a system clock to count the time between data packets.
    DateTime Millisecond { get; set; } = DateTime.Now;
    int DeltaT { get; set; } = 0;
    int PreviousMillisecond { get; set; }
    int DataStreamSize { get; set; } = 0;

    //
    // True will be autonomous mode. False will be manual mode.
    // Initialize this to manual mode. 'M' for manual. 'A' for autonomous.
    char ControlState { get; set; } = 'M';
    
    //
    // Payload release indicator.
    public bool PayloadRelease { get; set; }

    //
    // Flag to indicate when user connects to the radio.
    public bool RadioConnected { get; set; }

    //
    // Flag to indicate when a valid gps target coordinate has been set.
    public bool TargetSet { get; set; }

    //
    // Latitude and Longitude of target position.
    public float Latitude { get; set; }

    public float Longitude { get; set; }

    //
    // Set up a timer for the controller.
    DispatcherTimer PacketUpdate { get; set; } = new DispatcherTimer();
        
    //
    // Global variable to store all th data.
    DataPacket InputData { get; set; } = new DataPacket();

    //
    // Global variable to store output data.
    ControlOutDataPacket ControlOutData { get; set; } = new ControlOutDataPacket();

    TargetOutDataPacket TargetOutData { get; set; } = new TargetOutDataPacket();

    public MainWindow() {
      InitializeComponent();

      // Initialize COM Port GUI options and add handler for COM Port changes
      cbPorts.ItemsSource = SerialPortService.GetAvailableSerialPorts();
      SerialPortService.PortsChanged += (sender, eventArgs) => {
        cbPorts.Dispatcher.Invoke(DispatcherPriority.Normal, (Action)(() => {
          cbPorts.ItemsSource = SerialPortService.GetAvailableSerialPorts();
        }));
      };
      
      _COMPort.DataReceived += COMPortDataReceived;

      // Add handler to call closed function upon program exit
      Closed += new EventHandler(OnMainWindowClosed);
    }

    //
    // Com port received data event handler. Called by the operating system when
    // there is data available in the rx buffer.
    private void COMPortDataReceived(object sender, SerialDataReceivedEventArgs e) {
      int size;
      byte[] rawData;
      rawData = new byte[100];
      int currentMillisecond;

      //
      // Packet received! Get the current time.
      Millisecond = DateTime.Now;
      currentMillisecond = Millisecond.Millisecond;
      DeltaT = currentMillisecond - PreviousMillisecond;
      if (DeltaT < 0) DeltaT = DeltaT + 1000;

      PreviousMillisecond = currentMillisecond;

      //
      // Get the size of the incoming buffer.
      size = _COMPort.BytesToRead;
      DataStreamSize = size;

      //
      // Make sure we have a full packet, before updating.
      if (size > 77) {
        if (size > 100) {
          _COMPort.DiscardInBuffer();
        } else {
          //
          // Read the data from the incoming buffer.
          _COMPort.Read(rawData, 0, size);

          InputData.FromBytes(rawData);
          Dispatcher?.Invoke(() => UpdateGUI());
        }
      }
    }

    //
    // Updates the GUI with the new data values in the struct DataPacket.
    private void UpdateGUI() {
      //
      // Set the update rate.
      updateRate.Text = DeltaT.ToString();
      dataStream.Text = DataStreamSize.ToString();

      //
      // Update the GUI.
      UTC.Text = InputData.UTC.ToString();
      GPSLatitude.Text = InputData.Latitude.ToString();
      GPSLongitude.Text = InputData.Longitude.ToString();
      AltitudeASL.Text = InputData.Altitude.ToString();
      AltitudeAGL.Text = "0.000"; // TODO: Add a ground level feature.
      accelX.Text = InputData.AccelX.ToString();
      accelY.Text = InputData.AccelY.ToString();
      accelZ.Text = InputData.AccelZ.ToString();
      velX.Text = InputData.VelX.ToString();
      velY.Text = InputData.VelY.ToString();
      velZ.Text = InputData.VelZ.ToString();
      posX.Text = InputData.PosX.ToString();
      posY.Text = InputData.PosY.ToString();
      posZ.Text = InputData.PosZ.ToString();
      FlyOrDrive.Text = (InputData.Movement == 'D') ? "DRIVING" : "FLYING";
      Roll.Text = InputData.Roll.ToString();
      Pitch.Text = InputData.Pitch.ToString();
      Yaw.Text = InputData.Yaw.ToString();
      GndMtr1.Background = InputData.GroundMeter1 ? Brushes.GreenYellow : Brushes.Red;
      GndMtr2.Background = InputData.GroundMeter2 ? Brushes.GreenYellow : Brushes.Red;
      AirMtr1.Background = InputData.AirMotor1 ? Brushes.GreenYellow : Brushes.Red;
      AirMtr2.Background = InputData.AirMotor2 ? Brushes.GreenYellow : Brushes.Red;
      AirMtr3.Background = InputData.AirMotor3 ? Brushes.GreenYellow : Brushes.Red;
      AirMtr4.Background = InputData.AirMotor4 ? Brushes.GreenYellow : Brushes.Red;
      USensor1.Background = InputData.uS1 ? Brushes.GreenYellow : Brushes.Red;
      USensor2.Background = InputData.uS2 ? Brushes.GreenYellow : Brushes.Red;
      USensor3.Background = InputData.uS3 ? Brushes.GreenYellow : Brushes.Red;
      USensor4.Background = InputData.uS4 ? Brushes.GreenYellow : Brushes.Red;
      USensor5.Background = InputData.uS5 ? Brushes.GreenYellow : Brushes.Red;
      USensor6.Background = InputData.uS6 ? Brushes.GreenYellow : Brushes.Red;
      if (InputData.PayloadBay) {
        PayloadDeployed.Background = Brushes.GreenYellow;
        PayloadDeployed.Text = "Deployed";
      } else {
        PayloadDeployed.Background = Brushes.Red;
      }
    }

    //
    // Send data packet to the platform.
    public void SendPacket() {
      switch (ControlState) {
        case 'A': {
            byte[] data = TargetOutData.GetBytes();

            //
            // Write the data.
            _COMPort.Write(data, 0, data.Length);

            break;
          }
        case 'M': {
            byte[] datam = ControlOutData.GetBytes();

            //
            // Write the data.
            _COMPort.Write(datam, 0, datam.Length);

            break;
          }
      }
    }

    //
    // controller timer event handler. Called every 250 ms to check the 
    // state of the controller. 
    private void UpdateTimerTick(object sender, EventArgs e) {
      switch (ControlState) {
        case 'A': { // Autonomous mode
                    //
                    // Check if user has input coordinates yet or not.
            if (TargetSet) {
              //
              // Autonomous control. Just send lat and long and T for type to platform.
              TargetOutData.Type = 'T';
              TargetOutData.TargetLat = Latitude;
              TargetOutData.TargetLong = Longitude;
            } else {
              //
              // Send a '0', indicating bad data, ignore the target lat and long.
              TargetOutData.Type = '0';
            }
            break;
          }
        case 'M': { // Manual mode, control the platform with the controller.    
                    //
                    // Set the type of command.
            ControlOutData.Type = 'C';

            //
            // Check the throttle level. Ignore any x value on the right stick.
            // This will be a % from 0.0 to 1.0.
            ControlOutData.Throttle = (float)_controller.RightThumb.Y;

            //
            // Check if we are driving or flying.
            switch (InputData.Movement) {
              case 'D': {
                  //
                  // Travelling on the ground. Ignore pitch and roll.
                  ControlOutData.Throttle2 = (float)_controller.LeftThumb.Y;
                  ControlOutData.Pitch = 0.0F;
                  ControlOutData.Roll = 0.0F;
                  ControlOutData.Yaw = 0.0F;
                  break;
                }
              case 'F': {
                  //
                  // We are flying.
                  // Calculate the values of the left analog stick.
                  ControlOutData.Pitch = (float)_controller.LeftThumb.Y;
                  ControlOutData.Roll = (float)_controller.LeftThumb.X;

                  //
                  // Use the left and right triggers to calaculate yaw "rate". 
                  // Value ranges from 0 to 255 for triggers. 
                  if (_controller.RightTrigger > 0) {
                    ControlOutData.Yaw = _controller.RightTrigger;
                  } else if (_controller.LeftTrigger > 0) {
                    ControlOutData.Yaw = _controller.LeftTrigger * -1;
                  } else {
                    ControlOutData.Yaw = 0.0F;
                  }
                  break;
                }
            }

            //
            // Check the state of the buttons.
            if (Convert.ToInt16(_controller.ButtonState) == Convert.ToInt16(GamepadButtonFlags.Start)) {
              //
              // Start button is pressed, change from gnd travel to air travel.
              if (ControlOutData.FlyOrDrive == 'D') {
                ControlOutData.FlyOrDrive = 'F';
                ControlOutData.FDConfirm = 'F';
                FlyOrDrive.Text = "FLYING";
              } else {
                ControlOutData.FlyOrDrive = 'D';
                ControlOutData.FDConfirm = 'D';
                FlyOrDrive.Text = "DRIVING";
              }
            }

            //
            // Check if payload has been deployed.
            if (PayloadRelease) {
              ControlOutData.PayloadRelease = true;
              ControlOutData.PRConfirm = true;
            }
          }
          break;
      }
      //
      // Trigger a data packet send over the com port.
      if (RadioConnected && (_COMPort.BytesToRead == 0)) SendPacket();
    }

    //
    // Function to initialize the serial port on the machine.
    private void OnBtnCOMPortRefreshClick(object sender, RoutedEventArgs e) {
      cbPorts.ItemsSource = SerialPort.GetPortNames().OrderBy(x => x).ToArray();
      if (cbPorts.Items.Count > 0) cbPorts.SelectedIndex = 0;
      cbBaudRate.SelectedIndex = 0;
    }

    //
    // Connect to the Com Port button.
    private void OnBtnCOMPortConnectClick(object sender, RoutedEventArgs e) {
      if (btnCOMPortConnect.Content.ToString() == "Connect") {
        btnCOMPortConnect.Content = "Disconnect";

        _COMPort.PortName = cbPorts.Text;
        _COMPort.BaudRate = Convert.ToInt32(cbBaudRate.Text);
        _COMPort.DataBits = 8;
        _COMPort.StopBits = StopBits.One;
        _COMPort.Handshake = Handshake.None;
        _COMPort.Parity = Parity.None;

        //
        // Check if port is open already
        if (_COMPort.IsOpen) {
          _COMPort.Close();
          System.Windows.MessageBox.Show(string.Concat(_COMPort.PortName, " failed to open."));
        } else {
          _COMPort.Open();
        }

        //
        // COM Port is booted. Start keeping track of time.
        Millisecond = DateTime.Now;
        PreviousMillisecond = Millisecond.Millisecond;

        //
        // We have connection! 
        Radio.Background = Brushes.GreenYellow;
        Radio.Text = "Connected";
        RadioConnected = true;
      } else {
        btnCOMPortConnect.Content = "Connect";
        _COMPort.Close();

        //
        // Connection terminated.
        Radio.Background = Brushes.Red;
        Radio.Text = "Not Connected";
        RadioConnected = false;
      }
    }

    //
    // Change the operational mode of the platform.
    private void OnBtnModeClick(object sender, RoutedEventArgs e) {
      //
      // Get the color of the autotonomous box.
      switch (ControlState) {
        case 'A': {
            //
            // Currently in auto mode. Send command to switch to manual.
            AutoControl.Background = Brushes.Red;
            ManualControl.Background = Brushes.GreenYellow;

            //
            // Send command to platform.
            ControlState = 'M';
            break;
          }
        case 'M': {
            //
            // Currently in manual mode. Send comand to switch to auto.
            ManualControl.Background = Brushes.Red;
            AutoControl.Background = Brushes.GreenYellow;

            //
            // Send command to platform.
            ControlState = 'A';
            break;
          }
      }
    }

    //
    // Changes the update rate of the system (packets sent per second)
    private void OnBtnSetUpdateRateClick(object sender, RoutedEventArgs e) {
      //
      // Initialize the controller timer.
      PacketUpdate.Interval = TimeSpan.FromMilliseconds(250);
      PacketUpdate.Tick += UpdateTimerTick;

      //
      // Start the timer.
      PacketUpdate.Start();
    }

    //
    // Send the target location to the platform.
    private void OnBtnSendPacketClick(object sender, RoutedEventArgs e) {
      string targetLat; string targetLong;

      //
      // Get the data in the two text boxes.
      targetLat = TargetLatitude.Text;
      targetLong = TargetLongitude.Text;

      if (targetLat == String.Empty || targetLong == String.Empty) {
        //
        // User didn't input any data.
        // Maybe add a pop-up to say that...
        System.Windows.Forms.MessageBox.Show("You must input the target coordinates first!");
        return;
      } else {
        //
        // Signal that a target location has been set.
        TargetSet = true;

        //
        // This program assumes the user will enter valid data.
        // Convert this data to floating point.
        Latitude = Convert.ToSingle(targetLat);
        Longitude = Convert.ToSingle(targetLong);
        
        //
        // Set the current target position strings.
        CurrentTargetLatitude.Text = targetLat;
        CurrentTargetLongitude.Text = targetLong;
      }
    }

    //
    // Connect the selected controller
    private async void OnBtnConnectControllerClick(object sender, RoutedEventArgs e) {
      if (_controller.IsConnected) {
        await _controller.Disconnect();
        btnConnectController.Content = "Connect";
      } else {
        //
        // Initialize a controller using XINPUT.
        switch (cbController.Text) {
          case "1": {
              await _controller.Connect(UserIndex.One);
              break;
            }
          case "2": {
              await _controller.Connect(UserIndex.Two);
              break;
            }
          case "3": {
              await _controller.Connect(UserIndex.Three);
              break;
            }
          case "4": {
              await _controller.Connect(UserIndex.Four);
              break;
            }
          default: {
              await _controller.Connect(UserIndex.Any);
              break;
            }
        }
        if (_controller.IsConnected) {
          //
          // Change the button to say disconnect.
          btnConnectController.Content = $"Controller {_controller.UserIndex}";
        } else {
          System.Windows.MessageBox.Show("Could not connect to controller!");
        }
      }
    }

    private void OnControllerConnect(object sender, XboxController.ControllerEventArgs e) {

    }
    
    //
    // When button is pressed, manual deploy the payload. 
    private void OnBtnDeployPayloadClick(object sender, RoutedEventArgs e) {
      //
      // Deploy the payload.
      if (!PayloadRelease) {
        //
        // Set it to true.
        PayloadRelease = true;

        //
        // Change the GUI.
        DeployPayload.Content = "Deployed";
        PayloadDeployed.Background = Brushes.GreenYellow;
        PayloadDeployed.Text = "Deployed";
      }
    }

    //
    // End of program.
    private void OnMainWindowClosed(object sender, EventArgs e) {
      Dispose();
    }

    #region IDisposable Support
    private bool disposedValue = false; // To detect redundant calls

    protected virtual void Dispose(bool disposing) {
      if (!disposedValue) {
        if (disposing) {
          _COMPort.Dispose();
          _controller.Dispose();
          SerialPortService.CleanUp();
        }

        disposedValue = true;
      }
    }

    // This code added to correctly implement the disposable pattern.
    public void Dispose() {
      // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
      Dispose(true);
    }
    #endregion
  }
}