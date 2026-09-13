using Unity.Netcode.Components;
using UnityEngine;

/// <summary>
/// Owner-authoritative transform sync for responsive local movement in the multiplayer prototype.
/// </summary>
[DisallowMultipleComponent]
public class ClientNetworkTransform : NetworkTransform
{
    protected override bool OnIsServerAuthoritative() => false;
}
