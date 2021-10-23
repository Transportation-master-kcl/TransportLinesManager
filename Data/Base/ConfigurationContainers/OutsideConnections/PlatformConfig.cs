﻿using Klyte.Commons.Interfaces;
using Klyte.Commons.Utils;
using Klyte.TransportLinesManager.Utils;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;
using UnityEngine;

namespace Klyte.TransportLinesManager.Extensions
{
    public class PlatformConfig : IIdentifiable
    {
        [XmlAttribute("platformLaneId")]
        public long? Id { get; set; }

        [XmlIgnore]
        public uint PlatformLaneId => (uint)(Id >> 31);
        [XmlIgnore]
        public uint VehicleLaneId => (uint)(Id & 0x7FFFFFFF);

        [XmlElement("targetOutsideConnectionBuildings")]
        public NonSequentialList<OutsideConnectionNodeInfo> TargetOutsideConnections { get; set; } = new NonSequentialList<OutsideConnectionNodeInfo>();

        ~PlatformConfig() => ReleaseNodes();
        public void ReleaseNodes()
        {
            if (SimulationManager.exists)
            {
                var nodesToRelease = TargetOutsideConnections.SelectMany(x => new List<ushort> { x.Value.m_nodeOutsideConnection, x.Value.m_nodeStation }).Where(x => x != 0).GroupBy(x => x).Select(x => x.First());
                foreach (var key in TargetOutsideConnections.Keys.ToArray())
                {
                    TargetOutsideConnections[key] = null;
                }
                if (nodesToRelease.Count() > 0)
                {
                    SimulationManager.instance.AddAction(() =>
                    {
                        foreach (var node in nodesToRelease)
                        {
                            NetManager.instance.ReleaseNode(node);
                        }
                    });
                }
            }
        }
        public void UpdateStationNodes(ushort stationId)
        {
            var keys = TargetOutsideConnections.Keys.ToArray();
            foreach (var key in keys)
            {
                if (TargetOutsideConnections[key] is null && CreateConnectionLines(stationId, (ushort)key) is OutsideConnectionNodeInfo conn)
                {
                    TargetOutsideConnections[key] = conn;
                }
                else
                {
                    TargetOutsideConnections.Remove(key);
                }
            }
        }

        public void AddDestination(ushort stationId, ushort outsideConnectionId)
        {
            if (!TargetOutsideConnections.ContainsKey(outsideConnectionId))
            {
                if (CreateConnectionLines(stationId, outsideConnectionId) is OutsideConnectionNodeInfo conn)
                {
                    TargetOutsideConnections[outsideConnectionId] = conn;
                }
            }
        }
        public void RemoveDestination(ushort outsideConnectionId)
        {
            if (TargetOutsideConnections.ContainsKey(outsideConnectionId))
            {
                var nodesToRelease = new List<ushort> { TargetOutsideConnections[outsideConnectionId].m_nodeOutsideConnection, TargetOutsideConnections[outsideConnectionId].m_nodeStation }.Where(x => x != 0).GroupBy(x => x).Select(x => x.First());
                TargetOutsideConnections.Remove(outsideConnectionId);
                if (nodesToRelease.Count() > 0)
                {
                    SimulationManager.instance.AddAction(() =>
                    {
                        foreach (var node in nodesToRelease)
                        {
                            NetManager.instance.ReleaseNode(node);
                        }
                    });
                }
            }
        }

        private Vector3 StationPlatformPosition => NetManager.instance.m_lanes.m_buffer[VehicleLaneId].m_bezier.Position(.5f);

        private OutsideConnectionNodeInfo CreateConnectionLines(ushort stationId, ushort outsideConnectionId)
        {
            ref Building stationBuilding = ref BuildingManager.instance.m_buildings.m_buffer[stationId];
            ref Building outsideConnectionBuilding = ref BuildingManager.instance.m_buildings.m_buffer[outsideConnectionId];
            if ((stationBuilding.Info.m_buildingAI is TransportStationAI tsai) && (outsideConnectionBuilding.m_flags & Building.Flags.IncomingOutgoing) != Building.Flags.None)
            {
                var stationPlatformPosition = StationPlatformPosition;
                var result = new OutsideConnectionNodeInfo();
                NetManager instance = NetManager.instance;
                if (tsai.CreateConnectionNode(out result.m_nodeStation, stationPlatformPosition))
                {
                    if ((stationBuilding.m_flags & Building.Flags.Active) == Building.Flags.None)
                    {
                        instance.m_nodes.m_buffer[result.m_nodeStation].m_flags |= NetNode.Flags.Disabled;
                    }
                    instance.UpdateNode(result.m_nodeStation);
                    instance.m_nodes.m_buffer[result.m_nodeStation].m_nextBuildingNode = stationBuilding.m_netNode;
                    stationBuilding.m_netNode = result.m_nodeStation;
                }
                Building.Flags incomingOutgoing = ((outsideConnectionBuilding.m_flags & Building.Flags.IncomingOutgoing) != Building.Flags.Incoming) ? Building.Flags.Incoming : Building.Flags.Outgoing;
                Vector3 outsideConnectionPlatformPosition = TransportStationAIExtension.FindStopPosition(outsideConnectionId, ref outsideConnectionBuilding, incomingOutgoing);
                if (tsai.CreateConnectionNode(out result.m_nodeOutsideConnection, outsideConnectionPlatformPosition))
                {
                    if ((stationBuilding.m_flags & Building.Flags.Active) == Building.Flags.None)
                    {
                        instance.m_nodes.m_buffer[result.m_nodeOutsideConnection].m_flags |= NetNode.Flags.Disabled;
                    }
                    instance.UpdateNode(result.m_nodeOutsideConnection);
                    instance.m_nodes.m_buffer[result.m_nodeOutsideConnection].m_nextBuildingNode = stationBuilding.m_netNode;
                    stationBuilding.m_netNode = result.m_nodeOutsideConnection;
                }
                if (result.m_nodeStation != 0 && result.m_nodeOutsideConnection != 0)
                {
                    if ((outsideConnectionBuilding.m_flags & Building.Flags.Incoming) != Building.Flags.None)
                    {
                        if (tsai.CreateConnectionSegment(out result.m_segmentToStation, result.m_nodeStation, result.m_nodeOutsideConnection, 0))
                        {
                            instance.m_segments.m_buffer[result.m_segmentToStation].m_flags |= NetSegment.Flags.Untouchable;
                            instance.UpdateSegment(result.m_segmentToStation);
                        }
                    }
                    if ((outsideConnectionBuilding.m_flags & Building.Flags.Outgoing) != Building.Flags.None)
                    {
                        if (tsai.CreateConnectionSegment(out result.m_segmentToOutsideConnection, result.m_nodeOutsideConnection, result.m_nodeStation, 0))
                        {
                            instance.m_segments.m_buffer[result.m_segmentToOutsideConnection].m_flags |= NetSegment.Flags.Untouchable;
                            instance.UpdateSegment(result.m_segmentToOutsideConnection);
                        }
                    }
                    return result;
                }
                else
                {
                    instance.ReleaseNode(result.m_nodeStation);
                    instance.ReleaseNode(result.m_nodeOutsideConnection);
                }
            }
            return null;
        }

    }
}
