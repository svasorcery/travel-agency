namespace Travel.Modules.Support.Infrastructure.Retention;

public sealed class SupportRetentionStatus
{
    private int failed;
    public bool LastPassFailed => Volatile.Read(ref failed) == 1;

    public void SetFailure(bool value) => Volatile.Write(ref failed, value ? 1 : 0);
}
