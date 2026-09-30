"""Runs copied actual NetAuth domain source in a disposable package-free net10 project."""
from pathlib import Path
import hashlib
import json
import shutil
import subprocess
import tempfile

PROJECT = Path(__file__).resolve().parents[4]
SOURCES = ["src/NetAuth0.Domain/Common/Entity.cs", "src/NetAuth0.Domain/Identity/IdentityEntities.cs"]
PROGRAM = r"""
using NetAuth0.Domain.Identity;
using System.Text.Json;
int count = 0;
void Check(bool condition, string name) { count++; if (!condition) throw new Exception(name); }
var now = new DateTimeOffset(2026, 6, 9, 12, 0, 0, TimeSpan.Zero);
var tenant = Guid.NewGuid(); var app = Guid.NewGuid(); var userId = Guid.NewGuid(); var family = Guid.NewGuid();
var code = new AuthorizationCode("synthetic-hash", tenant, app, userId, "https://client.invalid/callback", "read", null, "challenge", "S256", now.AddMinutes(5));
Check(code.IsActive(now), "code initially active");
Check(!code.IsActive(now.AddMinutes(5)), "code exact expiry inactive");
code.Consume(now.AddMinutes(1));
Check(!code.IsActive(now.AddMinutes(2)), "consumed inactive");
code.Consume(now.AddMinutes(3));
Check(code.ConsumedAt == now.AddMinutes(3), "consume overwrites characterization");
var token = new RefreshToken("synthetic-refresh-hash", family, tenant, app, userId, "read", now.AddDays(1));
Check(token.IsActive(now), "refresh active");
Check(!token.IsActive(now.AddDays(1)), "refresh exact expiry");
var replacement = Guid.NewGuid(); token.RotateTo(replacement, now.AddMinutes(1));
Check(token.ReplacedById == replacement, "replacement linked");
Check(token.RevokedAt == now.AddMinutes(1), "rotation revokes");
Check(!token.IsActive(now.AddMinutes(2)), "rotated inactive");
token.Revoke(now.AddMinutes(3));
Check(token.RevokedAt == now.AddMinutes(1), "revoke first timestamp");
token.MarkReuse(now.AddMinutes(4)); token.MarkReuse(now.AddMinutes(5));
Check(token.ReuseDetectedAt == now.AddMinutes(4), "reuse first timestamp");
Check(token.RevokedAt == now.AddMinutes(1), "reuse preserves revocation");
var other = Guid.NewGuid(); token.RotateTo(other, now.AddMinutes(6));
Check(token.ReplacedById == other && token.RevokedAt == now.AddMinutes(6), "repeat rotation overwrites characterization");
var user = new User(tenant, "  Learner@EXAMPLE.INVALID  ", " Learner ", "synthetic-not-a-password-hash");
Check(user.Email == "learner@example.invalid", "email normalized");
Check(user.DisplayName == " Learner ", "display name not trimmed");
var stamp = user.SecurityStamp;
user.RegisterFailure(now, 2, TimeSpan.FromMinutes(10));
Check(user.FailedAccessCount == 1 && user.LockoutEndsAt == null, "before threshold");
user.RegisterFailure(now.AddMinutes(1), 2, TimeSpan.FromMinutes(10));
Check(user.LockoutEndsAt == now.AddMinutes(11), "threshold lockout");
user.RegisterFailure(now.AddMinutes(2), 2, TimeSpan.FromMinutes(10));
Check(user.LockoutEndsAt == now.AddMinutes(12), "later failure extends");
user.ResetFailures();
Check(user.FailedAccessCount == 0 && user.LockoutEndsAt == null, "reset failures");
user.Block(); Check(user.IsBlocked, "blocked"); user.Unblock();
Check(!user.IsBlocked && user.SecurityStamp == stamp, "unblock preserves stamp");
var client = new ClientApplication(tenant, "Synthetic", ClientApplicationType.Spa, "synthetic-client", null);
client.Configure(false, false, -1, 0);
Check(client.AccessTokenLifetimeMinutes == -1 && client.IdTokenLifetimeMinutes == -1 && client.RefreshTokenLifetimeDays == 0, "configure no bounds characterization");
var recovery = new RecoveryCode(user.Id, "synthetic-recovery-hash"); recovery.Use(now); recovery.Use(now.AddMinutes(1));
Check(recovery.UsedAt == now.AddMinutes(1), "recovery repeated use overwrites");
var enrollment = new MfaEnrollment(user.Id, "synthetic-not-a-real-secret"); enrollment.Verify(now); enrollment.Revoke(now.AddMinutes(1)); enrollment.Verify(now.AddMinutes(2));
Check(enrollment.VerifiedAt == now.AddMinutes(2) && enrollment.RevokedAt == now.AddMinutes(1), "enrollment methods independent");
Console.WriteLine(JsonSerializer.Serialize(new { assertions = count, result = "passed", scope = "copied actual domain source; no OAuth service, database or token signing" }));
"""

def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

def main():
    original = {name: digest(PROJECT / name) for name in SOURCES}
    temp = Path(tempfile.mkdtemp(prefix="netauth-domain-course-"))
    try:
        for index, name in enumerate(SOURCES):
            shutil.copyfile(PROJECT / name, temp / (str(index) + "-" + Path(name).name))
        (temp / "Lab.csproj").write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><Nullable>enable</Nullable><ImplicitUsings>enable</ImplicitUsings></PropertyGroup></Project>', encoding="utf-8")
        (temp / "NuGet.Config").write_text('<configuration><packageSources><clear /></packageSources></configuration>', encoding="utf-8")
        (temp / "Program.cs").write_text(PROGRAM, encoding="utf-8")
        result = subprocess.run(["dotnet", "run", "--project", "Lab.csproj", "--verbosity", "quiet"], cwd=temp, text=True, capture_output=True)
        print(result.stdout)
        if result.returncode:
            print(result.stderr)
            raise SystemExit(result.returncode)
        assert all(digest(PROJECT / name) == value for name, value in original.items())
        print(json.dumps({"source_hashes": original, "original_sources_unchanged": True}))
    finally:
        resolved = temp.resolve()
        assert resolved.parent == Path(tempfile.gettempdir()).resolve()
        assert resolved.name.startswith("netauth-domain-course-")
        shutil.rmtree(resolved)

if __name__ == "__main__":
    main()
