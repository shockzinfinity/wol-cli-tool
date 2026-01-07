using System.CommandLine;
using System.CommandLine.Parsing;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace WolCliTool;

internal class Program
{
  private static async Task<int> Main(string[] args)
  {
    var macOption = new Option<string>("--mac")
    { Description = "대상 장비의 MAC 주소 (예: AA-BB-CC-DD-EE-FF 또는 AABBCCDDEEFF)", Required = true };

    var ipOption = new Option<IPAddress>("--ip")
    {
      DefaultValueFactory = _ => IPAddress.Broadcast,
      Description = "대상 장비의 IP 주소 (기본값: 브로드캐스트 주소)",
      CustomParser = result =>
      {
        var token = result.Tokens.Count > 0
        ? result.Tokens[0].Value
        : null;

        if (string.IsNullOrWhiteSpace(token))
          return IPAddress.Broadcast;

        if (IPAddress.TryParse(token, out var ip))
          return ip;

        result.AddError($"Invalid value for --ip: {token}");
        return null;
      }
    };

    var portOption = new Option<int>("--port")
    {
      DefaultValueFactory = _ => 9,
      Description = "UDP 포트 (기본: 9)"
    };

    var repeatOption = new Option<int>("--repeat")
    {
      DefaultValueFactory = _ => 1,
      Description = "매직 패킷 전송 반복 횟수 (기본: 3)"
    };

    var intervalOption = new Option<int>("--interval")
    {
      DefaultValueFactory = _ => 500,
      Description = "패킷 전송 간 대기 시간 (기본: 500ms)"
    };

    var verboseOption = new Option<bool>("--verbose")
    {
      DefaultValueFactory = _ => false,
      Description = "상세 로그 출력 여부"
    };

    var untilUpOption = new Option<int>("--until-up")
    {
      DefaultValueFactory = _ => 0,
      Description = "지정 초 동안 대상이 응답할 때까지 주기적으로 시도 (0이면 비활성)"
    };

    var rootCommand = new RootCommand("Wake-on-LAN CLI Tool")
        {
          macOption, ipOption, portOption, repeatOption, intervalOption, verboseOption, untilUpOption
        };

    rootCommand.SetAction(
      async (ParseResult parsedResults, CancellationToken token) =>
      {
        var mac = parsedResults.GetValue(macOption);
        var ip = parsedResults.GetValue(ipOption);
        var port = parsedResults.GetValue(portOption);
        var repeat = parsedResults.GetValue(repeatOption);
        var interval = parsedResults.GetValue(intervalOption);
        var verbose = parsedResults.GetValue(verboseOption);
        var untilUp = parsedResults.GetValue(untilUpOption);

        var macBytes = ParseMacAddress(mac); // MAC 파싱
        var packet = BuildMagicPacket(macBytes); // 매직 패킷 생성

        await SendMagicPacketAsync(packet, ip, port, repeat, interval, verbose); // 전송

        if (untilUp > 0)
        {
          var end = DateTime.UtcNow.AddSeconds(untilUp);
          using var pinger = new Ping();
          while (DateTime.UtcNow < end)
          {
            try
            {
              var reply = await pinger.SendPingAsync(ip, 1000);
              if (reply.Status == IPStatus.Success)
              {
                if (verbose) Console.WriteLine("Target is up.");
                break;
              }
            }
            catch { /* ignore */}

            await SendMagicPacketAsync(packet, ip, port, 2, interval, verbose);
          }
        }

        return 0;
      });

    return await rootCommand.Parse(args).InvokeAsync();
  }

  /// <summary>
  /// MAC 문자열을 바이트 배열로 변환 (허용 형식: AA-BB-CC-DD-EE-FF, AABBCCDDEEFF 등)
  /// </summary>
  private static byte[] ParseMacAddress(string mac)
  {
    var hex = mac.Replace("-", "").Replace(":", "");
    if (hex.Length != 12)
      throw new ArgumentException("MAC 주소는 12자리 16진수 문자열이어야 합니다.", nameof(mac));
    var bytes = new byte[6];
    for (int i = 0; i < 6; i++)
    {
      bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
    }

    return bytes;
  }

  /// <summary>
  /// 매직 패킷 생성: 6 x 0xFF + 16 x MAC
  /// </summary>
  private static byte[] BuildMagicPacket(byte[] macAddress)
  {
    var packet = new byte[6 + 16 * macAddress.Length];
    // 6 바이트 0xFF
    for (int i = 0; i < 6; i++)
    {
      packet[i] = 0xFF;
    }
    // MAC 주소 16회 반복
    for (int i = 0; i < 16; i++)
    {
      Buffer.BlockCopy(macAddress, 0, packet, 6 + i * macAddress.Length, macAddress.Length);
    }

    return packet;
  }

  /// <summary>
  /// UDP 브로드캐스트 소켓 생성 후 매직 패킷 전송
  /// </summary>
  private static async Task SendMagicPacketAsync(byte[] packet, IPAddress ip, int port, int repeat = 1, int interval = 500, bool verbose = false, CancellationToken? cancellationToken = null)
  {
    using var udpClient = new UdpClient();
    udpClient.EnableBroadcast = true; // 브로드캐스트 활성화
    var endpoint = new IPEndPoint(ip, port);

    for (int i = 0; i < repeat; i++)
    {
      if (cancellationToken?.IsCancellationRequested == true)
        break;

      if (verbose)
        Console.WriteLine($"[{i + 1}/{repeat}] Sending magic packet to {endpoint}...");

      await udpClient.SendAsync(packet, packet.Length, endpoint);

      // 마지막 전송 후에는 대기하지 않음
      if (i < repeat - 1)
        await Task.Delay(interval, cancellationToken ?? CancellationToken.None); // 지정된 간격만큼 대기
    }

    if (verbose)
      Console.WriteLine("Magic packet sent successfully.");
  }
}