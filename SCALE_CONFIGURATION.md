# Scale Configuration Guide

## USB POS Scale Integration

This application supports integration with USB POS scales using the RS-232C protocol (compatible with CAS PD-2Z and similar scales).

### Hardware Setup

1. **Connect the Scale:**
   - Plug the scale into a USB port on your computer
   - Windows will automatically install drivers for the USB-to-Serial adapter
   - Note the COM port number assigned (e.g., COM3, COM4)

2. **Find Your COM Port:**
   - Open Device Manager (Windows Key + X, then select Device Manager)
   - Expand "Ports (COM & LPT)"
   - Look for your scale (may be listed as "USB Serial Port" or similar)
   - Note the COM port number (e.g., COM3)

### Software Configuration

1. **Edit `appsettings.json`:**

```json
{
  "Scale": {
    "PortName": "COM3",        // Change to your COM port
    "BaudRate": 9600,          // Common: 9600, 4800, 2400
    "DataBits": 8,             // Usually 8
    "Parity": "None",          // Options: None, Odd, Even
    "StopBits": "One",         // Options: One, Two
    "Enabled": true            // Set to false to use simulation mode
  }
}
```

2. **Common Settings for CAS PD-2Z Compatible Scales:**
   - Baud Rate: 9600
   - Data Bits: 8
   - Parity: None
   - Stop Bits: One

### Testing the Connection

1. Start the application
2. Look at the Live Reading card - it should show:
   - **Green indicator**: "Scale Connected (COM#)" - Scale is working
   - **Yellow indicator**: "Scale Disconnected - Simulation Mode" - Scale not found, using simulated data
   - **"Simulation Mode"**: Scale is disabled in settings

### Troubleshooting

#### Scale Not Connecting

1. **Verify COM Port:**
   ```bash
   # In appsettings.json, update PortName to match Device Manager
   "PortName": "COM3"  // or COM4, COM5, etc.
   ```

2. **Check Scale Settings:**
   - Some scales require configuration via DIP switches
   - Verify the scale is set to continuous output mode
   - Check that the baud rate matches your scale's settings

3. **Test with Terminal Software:**
   - Download PuTTY or another serial terminal
   - Connect to the COM port with your settings
   - You should see continuous weight data

4. **Permission Issues:**
   - Ensure your application has permission to access COM ports
   - Try running Visual Studio as Administrator

5. **Check Logs:**
   - Look at the application console output for error messages
   - Check for "Scale reader service started successfully" message

#### Weight Not Updating

1. **Verify Scale Output Format:**
   - The scale may use a different protocol
   - Contact the manufacturer for the protocol manual
   - The `ScaleReaderService.cs` may need adjustment

2. **Common Data Formats:**
   ```
   ST,GS,+00000lb     // Stable, Gross, weight in pounds
   ST,NT,+00000lb     // Stable, Net, weight in pounds
   +00000             // Simple weight format
   ```

### Disabling Physical Scale

To run in simulation mode:

```json
{
  "Scale": {
    "Enabled": false
  }
}
```

### Advanced Configuration

If your scale uses a different protocol, you may need to modify `ScaleReaderService.cs`:

1. Update the `ProcessScaleLine()` method to match your scale's data format
2. Adjust the `ParseWeightString()` method for your unit format
3. Test thoroughly before deploying to production

### Getting the Protocol Manual

Most manufacturers provide protocol documentation:
- Contact the manufacturer with your order number
- Check the manufacturer's website for downloads
- Look for "Communication Protocol" or "RS-232 Specification"

### Support

For scale-specific issues:
1. Verify the scale works with other software (PuTTY, manufacturer tools)
2. Double-check all configuration settings
3. Ensure the USB cable supports data (not just power)
4. Try a different USB port
5. Restart the application after configuration changes
