using SerialPortLib;

var bms = new SerialPortInput();
bms.SetPort("/dev/ttyUSB0", 115200);

bms.ConnectionStatusChanged += (object _, ConnectionStatusChangedEventArgs e) =>
{
    if (e.Connected)
        bms.QueryData();
};

bms.MessageReceived += async (object _, MessageReceivedEventArgs e) =>
{
    Console.Clear();

    var response = e.Data[11..]; //skip the first 10 bytes
    var cellCount = response[1] / 3; //pos 1 is total cell bytes length. 3 bytes per cell.
    if (cellCount is 0 or > 16) return;
    Console.WriteLine($"cell count:{cellCount}");

    int currentPos = 3;
    for (int i = 1; currentPos <= response.Length - 2 && i <= cellCount; i++)
    {
        //cell voltage groups (of 3 bytes) start at pos 2
        //first cell voltage starts at position 3 (pos 2 is cell number). voltage value is next 2 bytes.
        // ex: .....,1,X,X,2,Y,Y,3,Z,Z
        var voltage = Convert.ToDecimal(response.ReadValue(currentPos, 2)) / 1000;

        if (i < cellCount)
            currentPos += 3;

        Console.WriteLine($"cell {i}: {voltage:0.000} V");
    }

    currentPos += 3;
    var mosTemp = response.ReadValue(currentPos, 2);
    currentPos += 3;
    var probe1Temp = response.ReadValue(currentPos, 2);
    currentPos += 3;
    var probe2Temp = response.ReadValue(currentPos, 2);
    Console.WriteLine($"mos temp: {mosTemp} C | t1: {probe1Temp} C | t2: {probe2Temp} C");

    currentPos += 3;
    var packVoltage = Convert.ToDecimal(response.ReadValue(currentPos, 2)) / 100;
    Console.WriteLine($"pack voltage: {packVoltage:00.0} V");

    currentPos += 3;
    var currentAmps = Convert.ToDecimal(response.ReadValue(currentPos, 2)) / 1000; //this value is not right :-(
    Console.WriteLine($"current: {currentAmps:00.0} <-wrong!");

    currentPos += 3;
    var capacityPct = response.ReadValue(currentPos, 1);
    Console.WriteLine($"capacity: {capacityPct} %");

    currentPos += 103;
    var capacitySetting = response.ReadValue(currentPos, 4);
    Console.WriteLine($"pack capacity: {capacitySetting} Ah");

    var availableCapacity = Convert.ToDouble(capacitySetting) / 100 * capacityPct;
    Console.WriteLine($"available capacity: {availableCapacity:000.0} Ah");

    await Task.Delay(1000);
    bms.QueryData();
};

bms.Connect();

public static class Extensions
{
    const string commandHex = "4E5700130000000006030000000000006800000129";

    public static void QueryData(this SerialPortInput port)
    {
        port.SendMessage(Convert.FromHexString(commandHex));
    }

    public static short ReadValue(this byte[] data, int startPos, short bytesToRead)
    {
        var endPos = startPos + bytesToRead;
        return short.Parse(BitConverter.ToString(data[startPos..endPos]).Replace("-", ""), NumberStyles.HexNumber);
    }
}