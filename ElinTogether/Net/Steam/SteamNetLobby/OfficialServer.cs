using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using HeathenEngineering.SteamworksIntegration;
using Steamworks;
using UnityEngine;

namespace ElinTogether.Net.Steam;

/// <summary>
///     The project's own public game (user, 2026-10-27): the "Elin Together" Steam account hosts a public game anyone
///     can join, shown first in the list of games as the official server. A lobby's name and data are whatever its
///     host writes, and Steam tells a lobby's owner only to its members: the official game signs its lobby id with a
///     key kept on the PC that hosts it (ElinMP/official_server.key, never published), every game checks the
///     signature with the public half below. A game calling itself "Elin Together" is not shown as official
/// </summary>
internal static class OfficialServer
{
    internal const string SignatureKey = "EmpOfficial";

    // the "Elin Together" Steam account: only it signs, so that the key alone, copied elsewhere, makes no official game
    internal const ulong Account = 76561198768728915;

    // a public game can be big: the lobby of an ordinary game holds 16
    internal const int MaxPlayers = 100;

    private const string PublicKey =
        "<RSAKeyValue><Modulus>3jmy3n0WQfj1U3Ob9h+MEB/ehwuF79byRagfstI4uynV7cdi0JwOB9wJtBNMgJlPXKthObjEZFCwhdTxMZQy+mCVTGtgKsmAiulMFK3wMRkuOwSde/mq7d9rt2tLiSVuV5Bjl1Mo59cLDR4knMkKna9zFeSx3aKVJ42lPwYWfYkxZOPvyZnnr47w6Hk0t0xJRPwHqtohqJ6Jg07Rqyrahgle8YOGtW9moz+p/kqikQeMDLC0U5m0gLdcSvnirfG6OpgrKCPjnbOPH7zqEsTEh1e1lUCvXN/A9zCI8pYY7YwB9e5/H6cbZq/lOUVZWko/xOucNroxKABIte565EEEWQ==</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>";

    private static string KeyPath => Path.Combine(Application.persistentDataPath, "ElinMP", "official_server.key");

    /// <summary>
    ///     This game can host the official server: the Elin Together account, with the key on this PC
    /// </summary>
    internal static bool CanHost()
    {
        try {
            return SteamUser.GetSteamID().m_SteamID == Account && File.Exists(KeyPath);
        } catch (Exception) {
            // (Steam not running)
            return false;
        }
    }

    /// <summary>
    ///     This game is the official server being open: the official server refuses imported characters (user,
    ///     2026-10-27), so ImportCharacter reads off whatever the host ticked
    /// </summary>
    internal static bool Hosting => (EmpConfig.Server.PublicGame.Value || EmpServer.Requested) && CanHost();

    /// <returns>the signature of that lobby, null when this game cannot sign</returns>
    internal static string? Sign(ulong lobbyId)
    {
        if (!CanHost()) {
            return null;
        }

        try {
            using var rsa = new RSACryptoServiceProvider();
            rsa.FromXmlString(File.ReadAllText(KeyPath));
            return Convert.ToBase64String(rsa.SignData(Message(lobbyId), "SHA256"));
        } catch (Exception ex) when (ex is IOException or CryptographicException or UnauthorizedAccessException or FormatException) {
            EmpLog.Warning(ex, "The official server key could not sign the lobby");
            return null;
        }
    }

    internal static bool Verify(ulong lobbyId, string? signature)
    {
        if (string.IsNullOrEmpty(signature)) {
            return false;
        }

        try {
            using var rsa = new RSACryptoServiceProvider();
            rsa.FromXmlString(PublicKey);
            return rsa.VerifyData(Message(lobbyId), "SHA256", Convert.FromBase64String(signature));
        } catch (Exception ex) when (ex is CryptographicException or FormatException) {
            return false;
        }
    }

    internal static bool IsOfficial(LobbyData lobby)
    {
        return Verify(lobby.SteamId.m_SteamID, lobby[SignatureKey]);
    }

    // (the lobby id, so that a signature copied onto another lobby is worth nothing)
    private static byte[] Message(ulong lobbyId)
    {
        return Encoding.UTF8.GetBytes($"elin-together-official:{lobbyId}");
    }
}
