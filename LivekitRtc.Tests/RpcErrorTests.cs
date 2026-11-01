// author: https://github.com/pabloFuente

namespace LiveKit.Rtc.Tests;

/// <summary>
/// Pure managed tests of <see cref="RpcError"/>.
/// </summary>
public class RpcErrorTests
{
    [Fact]
    public void FromProto_WithoutData_HasNullRpcData()
    {
        // The optional proto field used to be read as "" when unset
        var error = RpcError.FromProto(
            new LiveKit.Proto.RpcError { Code = 1500, Message = "boom" }
        );
        Assert.Equal(1500, error.Code);
        Assert.Equal("boom", error.Message);
        Assert.Null(error.RpcData);

        var withData = RpcError.FromProto(
            new LiveKit.Proto.RpcError
            {
                Code = 1500,
                Message = "boom",
                Data = "{}",
            }
        );
        Assert.Equal("{}", withData.RpcData);

        // The FFI sets the field to "" when the remote error carried no data
        var emptyData = RpcError.FromProto(
            new LiveKit.Proto.RpcError
            {
                Code = 1500,
                Message = "boom",
                Data = "",
            }
        );
        Assert.Null(emptyData.RpcData);
    }
}
