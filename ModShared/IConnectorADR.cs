﻿extern alias ADR;
using UnityEngine;

namespace Klyte.TransportLinesManager.ModShared
{
    internal abstract class IConnectorADR : MonoBehaviour
    {
        public abstract bool GetAddressStreetAndNumber(Vector3 sidewalk, Vector3 midPosBuilding, out int number, out string streetName);
    }
}