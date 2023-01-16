using SerialPortLib;
using System.Globalization;

var pollFrequencyMillis = 1000;
var cellVoltages = new Dictionary<byte, float>(); //key: cell number //val: cell voltage
var recentAmpReadings = new AmpValQueue(10); //avg value over 10 readings (~10secs)

var bms = new SerialPortInput();
bms.SetPort("/dev/ttyUSB0", 115200);

bms.ConnectionStatusChanged += (object _, ConnectionStatusChangedEventArgs e) =>
{
    if (e.Connected)
        bms.QueryData();

    Console.WriteLine($"CONNECTED: {e.Connected}");
};

bms.MessageReceived += (object _, MessageReceivedEventArgs evnt) =>
{
    Console.Clear();

    var data = evnt.Data.AsSpan();

    if (!data.IsValid())
    {
        Console.WriteLine("invalid data received!");
        Thread.Sleep(pollFrequencyMillis);
        bms.QueryData();
        return;
    }

    var res = data[11..]; //skip the first 10 bytes
    var cellCount = res[1] / 3; //pos 1 is total cell bytes length. 3 bytes per cell.

    Console.WriteLine($"cell count:{cellCount}");

    ushort pos = 0;
    for (byte i = 1; i <= cellCount; i++)
    {
        //cell voltage groups (of 3 bytes) start at pos 2
        //first cell voltage starts at position 3 (pos 2 is cell number). voltage value is next 2 bytes.
        // ex: .....,1,X,X,2,Y,Y,3,Z,Z
        //pos is increased by 3 bytes in order to skip the address/code byte
        cellVoltages[i] = res.Read2Bytes(pos += 3) / 1000f;

        Console.WriteLine($"cell {i}: {cellVoltages[i]:0.000} V");
    }

    var avgCellVoltage = cellVoltages.Values.Average();
    var minCell = cellVoltages.MinBy(x => x.Value);
    var maxCell = cellVoltages.MaxBy(x => x.Value);
    var cellDiff = maxCell.Value - minCell.Value;
    Console.WriteLine($"avg cell voltage: {avgCellVoltage:0.000} V");
    Console.WriteLine($"min cell: [{minCell.Key}] {minCell.Value:0.000} V");
    Console.WriteLine($"max cell: [{maxCell.Key}] {maxCell.Value:0.000} V");
    Console.WriteLine($"cell diff: {cellDiff:0.000} V");

    var mosTemp = res.Read2Bytes(pos += 3);
    var probe1Temp = res.Read2Bytes(pos += 3);
    var probe2Temp = res.Read2Bytes(pos += 3);
    Console.WriteLine($"mos temp: {mosTemp} C | t1: {probe1Temp} C | t2: {probe2Temp} C");

    var packVoltage = res.Read2Bytes(pos += 3) / 100f;
    Console.WriteLine($"pack voltage: {packVoltage:00.00} V");

    var rawVal = res.Read2Bytes(pos += 3);
    var isCharging = Convert.ToBoolean(int.Parse(Convert.ToString(rawVal, 2).PadLeft(16, '0')[..1])); //pick first bit of padded 16 bit binary representation and turn it in to a bool
    Console.WriteLine($"charging: {isCharging}");

    rawVal &= (1 << 15) - 1; //unset the MSB with a bitmask
    var ampVal = rawVal / 100f;
    recentAmpReadings.Enqueue(ampVal);
    var avgCurrentAmps = recentAmpReadings.GetAverage();
    Console.WriteLine($"current: {avgCurrentAmps:0.0} A");

    var capacityPct = Convert.ToUInt16(res[pos += 3]);
    Console.WriteLine($"capacity: {capacityPct} %");

    var isWarning = res.Read2Bytes(pos += 15) > 0;
    Console.WriteLine($"warning: {isWarning}");

    var packCapacity = res.Read4Bytes(pos += 88);
    Console.WriteLine($"pack capacity: {packCapacity} Ah");

    var availableCapacity = packCapacity / 100f * capacityPct;
    Console.WriteLine($"available capacity: {availableCapacity:0.0} Ah");

    var timeLeft = 0f;

    if (avgCurrentAmps > 0)
    {
        if (isCharging)
            timeLeft = (packCapacity - availableCapacity) / avgCurrentAmps;
        else
            timeLeft = availableCapacity / avgCurrentAmps;

        var tSpan = TimeSpan.FromHours(timeLeft);
        var totalHrs = (ushort)tSpan.TotalHours;
        Console.WriteLine($"time left: {totalHrs} Hrs {tSpan.Minutes} Mins");
    }

    var cRate = Math.Round(avgCurrentAmps / packCapacity, 2, MidpointRounding.AwayFromZero);
    Console.WriteLine($"c-rate: {cRate}");

    Thread.Sleep(pollFrequencyMillis);
    bms.QueryData();
};

bms.Connect();

public static class Extensions
{
    private static readonly byte[] cmd = Convert.FromHexString("4E5700130000000006030000000000006800000129");

    public static void QueryData(this SerialPortInput port)
    {
        port.SendMessage(cmd);
    }

    public static ushort Read2Bytes(this Span<byte> data, ushort startPos)
    {
        var hex = Convert.ToHexString(data.Slice(startPos, 2));
        return ushort.Parse(hex, NumberStyles.HexNumber);
    }

    public static uint Read4Bytes(this Span<byte> input, ushort startPos)
    {
        var hex = Convert.ToHexString(input.Slice(startPos, 4));
        return uint.Parse(hex, NumberStyles.HexNumber);
    }

    public static bool IsValid(this Span<byte> data)
    {
        if (data.Length < 8)
            return false;

        var header = Convert.ToHexString(data[..2]); //get hex from first 2 bytes

        if (header is not "4E57")
            return false;

        short crc_calc = 0;

        foreach (var b in data[0..^3])//sum up all bytes excluding the last 4 bytes
            crc_calc += b;

        //convert last 2 bytes to hex and get short from that hex
        var crc_lo = data[^2..^0].Read2Bytes(0);

        return crc_calc == crc_lo;
    }
}

public sealed class AmpValQueue : Queue<float>
{
    public int FixedCapacity { get; }
    public AmpValQueue(int fixedCapacity)
    {
        FixedCapacity = fixedCapacity;
    }

    public new void Enqueue(float val)
    {
        if (val > 0)
        {
            base.Enqueue(val);
            if (Count > FixedCapacity)
            {
                Dequeue();
            }
        }
        else
        {
            Clear();
        }
    }

    public float GetAverage()
    {
        return Count > 0
               ? this.Average()
               : 0;
    }
}