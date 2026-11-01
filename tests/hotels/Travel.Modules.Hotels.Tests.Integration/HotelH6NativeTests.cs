using System.Security.AccessControl;
using System.Security.Principal;
using Shouldly;
using Travel.Hotels.Fixture;

namespace Travel.Modules.Hotels.Tests.Integration;

public sealed class HotelH6NativeTests
{
    [Fact]
    public void Session_classification_is_scoped_and_refuses_ambiguous_names()
    {
        const string own = "TravelHotelsH6-0123456789abcdef0123456789abcdef";
        HotelsH6Native.OwnSession([], own).ShouldBe(HotelH6SessionStatus.Absent);
        HotelsH6Native
            .OwnSession([new("WPR_initiated_x_" + own, 0, 0, 0)], own)
            .ShouldBe(HotelH6SessionStatus.Owned);
        HotelsH6Native
            .OwnSession([new("WPR_initiated_other", 0, 0, 0)], own)
            .ShouldBe(HotelH6SessionStatus.Absent);
        HotelsH6Native
            .OwnSession([new("unexpected_" + own, 0, 0, 0)], own)
            .ShouldBe(HotelH6SessionStatus.Unknown);
    }

    [Fact]
    public void Read_only_native_ETW_query_decodes_current_visible_session_metadata()
    {
        if (!OperatingSystem.IsWindows())
            return;
        foreach (var session in HotelsH6Native.Sessions())
            session.Name.Length.ShouldBeInRange(1, 1024);
        // A normal token may see only a subset. This is ABI/read-only proof, not elevated isolation proof.
    }

    [Fact]
    public void Handoff_is_bound_to_relative_names_and_bytes()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "h6-source-test-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(path);
        try
        {
            File.WriteAllText(Path.Combine(path, "one"), "a");
            var first = HotelsH6Store.Fingerprint(path);
            File.WriteAllText(Path.Combine(path, "one"), "b");
            HotelsH6Store.Fingerprint(path).ShouldNotBe(first);
            File.WriteAllText(Path.Combine(path, "one"), "a");
            File.Move(Path.Combine(path, "one"), Path.Combine(path, "two"));
            HotelsH6Store.Fingerprint(path).ShouldNotBe(first);
        }
        finally
        {
            File.Delete(Path.Combine(path, "one"));
            File.Delete(Path.Combine(path, "two"));
            Directory.Delete(path);
        }
    }

    [Fact]
    public void Normal_user_has_read_only_inheritable_output_rights()
    {
        if (!OperatingSystem.IsWindows())
            return;
        var sid = WindowsIdentity.GetCurrent().User!.Value;
        var acl = HotelsH6Store.Security(sid);
        acl.AreAccessRulesProtected.ShouldBeTrue();
        var rule = acl.GetAccessRules(true, false, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .Single(r => OperatingSystem.IsWindows() && r.IdentityReference.Value == sid);
        (
            rule.FileSystemRights
            & (
                FileSystemRights.Write
                | FileSystemRights.Delete
                | FileSystemRights.ChangePermissions
            )
        ).ShouldBe((FileSystemRights)0);
        rule.InheritanceFlags.ShouldBe(
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit
        );
        Should.Throw<InvalidDataException>(() => HotelsH6Store.Location("../escape"));
    }
}
