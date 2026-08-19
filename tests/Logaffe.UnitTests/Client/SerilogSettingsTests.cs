using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Debugging;

namespace Logaffe.UnitTests.Client;

/// <summary>
/// The sink as an application configures it from a file, which is how most
/// applications configure Serilog.
/// </summary>
/// <remarks>
/// <para>
/// A settings binder reads the parameters of a configuration method and can
/// construct nothing, so <c>EntryDeliveryOptions</c> is out of its reach — and
/// an argument it cannot place it drops rather than refuses. Everything a sender
/// can set therefore has to be a parameter of its own, and this is the only test
/// that can say whether it is: from code every setting is reachable whether a
/// binder could see it or not.
/// </para>
/// <para>
/// One assertion covers the whole list, because the binder chooses the overload
/// that takes the arguments it was given. A parameter missing or a type it
/// cannot convert and it falls back to the two-argument method, where the queue
/// is ten thousand entries deep and nothing is dropped at all.
/// </para>
/// </remarks>
[Collection(nameof(SelfLogCollection))]
public sealed class SerilogSettingsTests
{
    private const string Settings = """
        {
          "Serilog": {
            "MinimumLevel": "Verbose",
            "WriteTo": [
              {
                "Name": "Logaffe",
                "Args": {
                  "installation": "https://logs.example.com",
                  "ingestToken": "lgf_i_test",
                  "instance": "ops/example",
                  "queueCapacity": 0,
                  "batchInterval": "00:00:02",
                  "flushTimeout": "00:00:03",
                  "deliveryTimeout": "00:00:04"
                }
              }
            ]
          }
        }
        """;

    /// <summary>
    /// A queue that holds nothing drops every entry and says which queue it was,
    /// which is what makes a setting visible here: nothing is delivered, so
    /// nothing about this test needs an installation to deliver to.
    /// </summary>
    [Fact]
    public void Every_setting_a_sender_can_write_reaches_the_delivery()
    {
        var selfLog = new ConcurrentQueue<string>();

        SelfLog.Enable(selfLog.Enqueue);

        try
        {
            var settings = new ConfigurationBuilder()
                .AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(Settings)))
                .Build();

            using (var logger = new LoggerConfiguration()
                .ReadFrom.Configuration(settings)
                .CreateLogger())
            {
                logger.Information("Started");
                logger.Information("And again");
            }
        }
        finally
        {
            SelfLog.Disable();
        }

        Assert.Contains(
            selfLog,
            said => said.Contains("the queue of 0 was full", StringComparison.Ordinal));
    }
}
