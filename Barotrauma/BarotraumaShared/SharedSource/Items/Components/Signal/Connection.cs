﻿using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class Connection
    {
        //how many wires can be linked to connectors by default
        private const int DefaultMaxWires = 5;

        //how many wires a player can link to this connection
        public readonly int MaxPlayerConnectableWires = 5;

        //how many wires can be linked to this connection in total
        public readonly int MaxWires = 5;

        public readonly string Name;
        public readonly LocalizedString DisplayName;

        private readonly HashSet<Wire> wires;
        public IReadOnlyCollection<Wire> Wires => wires;

        private readonly Item item;

        public readonly bool IsOutput;
        
        public readonly List<StatusEffect> Effects;

        public readonly List<ushort> LoadedWireIds;

        //The grid the connection is a part of
        public GridInfo Grid;

        //Priority in which power output will be handled - load is unaffected
        public PowerPriority Priority = PowerPriority.Default;

        public bool IsPower
        {
            get;
            private set;
        }

        private bool recipientsDirty = true;
        private readonly List<Connection> recipients = new List<Connection>();
        public List<Connection> Recipients
        {
            get
            {
                if (recipientsDirty) { RefreshRecipients(); }
                return recipients;
            }
        }

        public Item Item
        {
            get { return item; }
        }

        public ConnectionPanel ConnectionPanel
        {
            get;
            private set;
        }

        public override string ToString()
        {
            return "Connection (" + item.Name + ", " + Name + ")";
        }

        public Connection(ContentXElement element, ConnectionPanel connectionPanel, IdRemap idRemap)
        {

#if CLIENT
            if (connector == null)
            {
                connector = GUIStyle.GetComponentStyle("ConnectionPanelConnector").GetDefaultSprite();
                wireVertical = GUIStyle.GetComponentStyle("ConnectionPanelWire").GetDefaultSprite();
                connectionSprite = GUIStyle.GetComponentStyle("ConnectionPanelConnection").GetDefaultSprite();
                connectionSpriteHighlight = GUIStyle.GetComponentStyle("ConnectionPanelConnection").GetSprite(GUIComponent.ComponentState.Hover);
                screwSprites = GUIStyle.GetComponentStyle("ConnectionPanelScrew").Sprites[GUIComponent.ComponentState.None].Select(s => s.Sprite).ToList();
            }
#endif
            ConnectionPanel = connectionPanel;
            item = connectionPanel.Item;

            MaxWires = element.GetAttributeInt("maxwires", DefaultMaxWires);
            MaxWires = Math.Max(element.Elements().Count(e => e.Name.ToString().Equals("link", StringComparison.OrdinalIgnoreCase)), MaxWires);

            MaxPlayerConnectableWires = element.GetAttributeInt("maxplayerconnectablewires", MaxWires);
            wires = new HashSet<Wire>();

            IsOutput = element.Name.ToString() == "output";
            Name = element.GetAttributeString("name", IsOutput ? "output" : "input");

            string displayNameTag = "", fallbackTag = "";
            //if displayname is not present, attempt to find it from the prefab
            if (element.GetAttribute("displayname") == null)
            {
                foreach (var subElement in item.Prefab.ConfigElement.Elements())
                {
                    if (!subElement.Name.ToString().Equals("connectionpanel", StringComparison.OrdinalIgnoreCase)) { continue; }
                    
                    foreach (XElement connectionElement in subElement.Elements())
                    {
                        string prefabConnectionName = connectionElement.GetAttributeString("name", null);
                        string[] aliases = connectionElement.GetAttributeStringArray("aliases", Array.Empty<string>());
                        if (prefabConnectionName == Name || aliases.Contains(Name))
                        {
                            displayNameTag = connectionElement.GetAttributeString("displayname", "");
                            fallbackTag = connectionElement.GetAttributeString("fallbackdisplayname", "");
                        }
                    }
                }
            }
            else
            {
                displayNameTag = element.GetAttributeString("displayname", "");
                fallbackTag = element.GetAttributeString("fallbackdisplayname", null);
            }

            if (!string.IsNullOrEmpty(displayNameTag))
            {
                //extract the tag parts in case the tags contains variables
                string tagWithoutVariables = displayNameTag?.Split('~')?.FirstOrDefault();
                string fallbackTagWithoutVariables = fallbackTag?.Split('~')?.FirstOrDefault();
                //use displayNameTag if found, otherwise fallBack
                if (TextManager.ContainsTag(tagWithoutVariables))
                {
                    DisplayName = TextManager.GetServerMessage(displayNameTag);
                }
                else if (TextManager.ContainsTag(fallbackTagWithoutVariables))
                {
                    DisplayName = TextManager.GetServerMessage(fallbackTag);
                }
            }

            if (DisplayName.IsNullOrEmpty())
            {
#if DEBUG
                DebugConsole.ThrowError("Missing display name in connection " + item.Name + ": " + Name);
#endif
                DisplayName = Name;
            }

            IsPower = Name == "power_in" || Name == "power" || Name == "power_out";


            LoadedWireIds = new List<ushort>();
            foreach (var subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "link":
                        int id = subElement.GetAttributeInt("w", 0);
                        if (id < 0) { id = 0; }
                        if (LoadedWireIds.Count < MaxWires) { LoadedWireIds.Add(idRemap.GetOffsetId(id)); }

                        break;
                    case "statuseffect":
                        Effects ??= new List<StatusEffect>();
                        Effects.Add(StatusEffect.Load(subElement, item.Name + ", connection " + Name));
                        break;
                }
            }
        }

        public void SetRecipientsDirty()
        {
            recipientsDirty = true;
            if (IsPower) { Powered.ChangedConnections.Add(this); }
        }

        private void RefreshRecipients()
        {
            recipients.Clear();
            foreach (var wire in wires)
            {
                Connection recipient = wire.OtherConnection(this);
                if (recipient != null) { recipients.Add(recipient); }
            }
            recipientsDirty = false;
        }

        public Wire FindWireByItem(Item it)
            => Wires.FirstOrDefault(w => w.Item == it);

        public bool WireSlotsAvailable()
            => wires.Count < MaxWires;
        
        public bool TryAddLink(Wire wire)
        {
            if (wire is null
                || wires.Contains(wire)
                || !WireSlotsAvailable())
            {
                return false;
            }
            wires.Add(wire);
            return true;
        }

        public void DisconnectWire(Wire wire)
        {
            if (wire == null || !wires.Contains(wire)) { return; }

            var prevOtherConnection = wire.OtherConnection(this);
            if (prevOtherConnection != null)
            {
                //Change the connection grids or flag them for updating
                if (IsPower && prevOtherConnection.IsPower && Grid != null)
                {
                    //Check if both connections belong to a larger grid
                    if (prevOtherConnection.recipients.Count > 1 && recipients.Count > 1)
                    {
                        Powered.ChangedConnections.Add(prevOtherConnection);
                        Powered.ChangedConnections.Add(this);
                    }
                    else if (recipients.Count > 1)
                    {
                        //This wire was the only one at the other grid
                        prevOtherConnection.Grid?.RemoveConnection(prevOtherConnection);
                        prevOtherConnection.Grid = null;
                    }
                    else if (prevOtherConnection.recipients.Count > 1)
                    {
                        Grid?.RemoveConnection(this);
                        Grid = null;
                    }
                    else if (Grid.Connections.Count == 2)
                    {
                        //Delete the grid as these were the only 2 devices
                        Powered.Grids.Remove(Grid.ID);
                        Grid = null;
                        prevOtherConnection.Grid = null;
                    }
                }
                prevOtherConnection.recipientsDirty = true;
            }
            wires.Remove(wire);
            recipientsDirty = true;
        }
        
        public void ConnectWire(Wire wire)
        {
            if (wire == null || !TryAddLink(wire)) { return; }
            ConnectionPanel.DisconnectedWires.Remove(wire);
            var otherConnection = wire.OtherConnection(this);
            if (otherConnection != null)
            {
                //Set the other connection grid if a grid exists already
                if (Powered.ValidPowerConnection(this, otherConnection))
                {
                    if (Grid == null && otherConnection.Grid != null)
                    {
                        otherConnection.Grid.AddConnection(this);
                        Grid = otherConnection.Grid;
                    }
                    else if (Grid != null && otherConnection.Grid == null)
                    {
                        Grid.AddConnection(otherConnection);
                        otherConnection.Grid = Grid;
                    }
                    else
                    {
                        //Flag change so that proper grids can be formed
                        Powered.ChangedConnections.Add(this);
                        Powered.ChangedConnections.Add(otherConnection);
                    }
                }

                otherConnection.recipientsDirty = true;
            }
            recipientsDirty = true;
        }

        public void SendSignal(Signal signal)
        {
            foreach (var wire in wires)
            {
                Connection recipient = wire.OtherConnection(this);
                if (recipient == null) { continue; }
                if (recipient.item == this.item || signal.source?.LastSentSignalRecipients.LastOrDefault() == recipient) { continue; }

                signal.source?.LastSentSignalRecipients.Add(recipient);

                Connection connection = recipient;

                foreach (ItemComponent ic in recipient.item.Components)
                {
                    ic.ReceiveSignal(signal, connection);
                }

                if (recipient.Effects != null && signal.value != "0")
                {
                    foreach (StatusEffect effect in recipient.Effects)
                    {
                        recipient.Item.ApplyStatusEffect(effect, ActionType.OnUse, (float)Timing.Step);
                    }
                }
            }
        }
        
        public void ClearConnections()
        {
            if (IsPower && Grid != null)
            {
                Powered.ChangedConnections.Add(this);
                foreach (Connection c in recipients)
                {
                    Powered.ChangedConnections.Add(c);
                }
            }

            foreach (var wire in wires)
            {
                wire.RemoveConnection(this);
                recipientsDirty = true;
            }
            wires.Clear();
        }
        
        public void InitializeFromLoaded()
        {
            if (LoadedWireIds.Count == 0) { return; }
            
            for (int i = 0; i < LoadedWireIds.Count; i++)
            {
                if (!(Entity.FindEntityByID(LoadedWireIds[i]) is Item wireItem)) { continue; }

                var wire = wireItem.GetComponent<Wire>();
                if (wire != null && TryAddLink(wire))
                {
                    if (wire.Item.body != null) wire.Item.body.Enabled = false;
                    wire.Connect(this, false, false);
                    wire.FixNodeEnds();
                    recipientsDirty = true;
                }
            }
            LoadedWireIds.Clear();
        }


        public void Save(XElement parentElement)
        {
            XElement newElement = new XElement(IsOutput ? "output" : "input", new XAttribute("name", Name));

            foreach (var wire in wires.OrderBy(w => w.Item.ID))
            {
                newElement.Add(new XElement("link",
                    new XAttribute("w", wire.Item.ID.ToString())));
            }

            parentElement.Add(newElement);
        }
    }
}