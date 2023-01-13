using SerialPortLib;
using System.Globalization;

var currentAmpsQueue = new LimitedQueue<float>(10); //avg value over 10 readings (~10secs)

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

    short currentPos = 3;
    for (int i = 1; currentPos <= response.Length - 2 && i <= cellCount; i++)
    {
        //cell voltage groups (of 3 bytes) start at pos 2
        //first cell voltage starts at position 3 (pos 2 is cell number). voltage value is next 2 bytes.
        // ex: .....,1,X,X,2,Y,Y,3,Z,Z
        var voltage = response.ReadShort(currentPos) / 1000f;
        Console.WriteLine($"cell {i}: {voltage:0.000} V");

        if (i < cellCount)
            currentPos += 3;
    }

    currentPos += 3;
    var mosTemp = response.ReadShort(currentPos);
    currentPos += 3;
    var probe1Temp = response.ReadShort(currentPos);
    currentPos += 3;
    var probe2Temp = response.ReadShort(currentPos);
    Console.WriteLine($"mos temp: {mosTemp} C | t1: {probe1Temp} C | t2: {probe2Temp} C");

    currentPos += 3;
    var packVoltage = response.ReadShort(currentPos) / 100f;
    Console.WriteLine($"pack voltage: {packVoltage:00.0} V");

    currentPos += 3;
    var rawVal = response.ReadShort(currentPos);
    var isCharging = Convert.ToBoolean(rawVal & 0xFF00); //get MSB and convert it to bool
    Console.WriteLine($"Is Charging: {isCharging}");

    rawVal &= (1 << 15) - 1; //unset the MSB with a bitmask
    var currentAmps = rawVal / 100f;
    currentAmpsQueue.Enqueue(currentAmps);
    var avgCurrentAmps = currentAmpsQueue.Average();
    Console.WriteLine($"current: {avgCurrentAmps:0.0} A");

    currentPos += 3;
    var capacityPct = Convert.ToInt16(response[currentPos]);
    Console.WriteLine($"capacity: {capacityPct} %");

    currentPos += 103;
    var capacitySetting = response.ReadInt(currentPos);
    Console.WriteLine($"pack capacity: {capacitySetting} Ah");

    var availableCapacity = capacitySetting / 100f * capacityPct;
    Console.WriteLine($"available capacity: {availableCapacity:0.0} Ah");

    //var timeLeft = availableCapacity / avgCurrentAmps;
    //var tSpan = TimeSpan.FromHours(timeLeft);
    //Console.WriteLine($"Time Left: {tSpan.Hours}:{tSpan.Minutes}");

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

    public static short ReadShort(this byte[] input, short startPos)
    {
        var hex = Convert.ToHexString(input, startPos, 2);
        return short.Parse(hex, NumberStyles.HexNumber);
    }

    public static int ReadInt(this byte[] input, short startPos)
    {
        var hex = Convert.ToHexString(input, startPos, 4);
        return int.Parse(hex, NumberStyles.HexNumber);
    }
}

public sealed class LimitedQueue<T> : Queue<T>
{
    public int FixedCapacity { get; }
    public LimitedQueue(int fixedCapacity)
    {
        FixedCapacity = fixedCapacity;
    }

    public new void Enqueue(T item)
    {
        base.Enqueue(item);
        if (Count > FixedCapacity)
        {
            Dequeue();
        }
    }
}