using WorkerService1.Controller;
using WorkerService1.Model;
using WorkerService1.Services.Validator_data;

public class ConsensusService
{
    private readonly PeerService _peers;
    private readonly BlockchainService _chain;
    private readonly NodeDatabase _db;

    public ConsensusService(
        PeerService peers,
        BlockchainService chain,
        NodeDatabase db)
    {
        _peers = peers;
        _chain = chain;
        _db = db;
    }

    public Task<object> UserPairAsync(UserPairPayloadDto req)
    {
        var result = new
        {
            message = "User paired successfully",
            timestamp = req.Payload?.Timestamp ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            users = req.Payload?.Users,
            publicKey = req.PublicKey,
            ts = DateTime.UtcNow
        };

        return Task.FromResult((object)result);
    }

    public Task<bool> VerifyVote(VotePayloadDto req)
    {
        return Task.FromResult(true);
    }
}
