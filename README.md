# Flow Engineering Connector Example

Example DataMiner connector that demonstrates how to support generic flow engineering (FLE) tables via InterApp messages.

The objective is to create a mediation layer using generic tables. 
This allows to track which incoming and outgoing flows are passing through an element in a standardized way.
The tables are mostly populated with data that is coming from the device, but also extended with metadata from flow engineering itself.
Flows can be multicast streams, SDI or ASI connections, and more.
These tables are used by the MediaOps solution to show the 'as-is' path, which represents the actual route a specific signal takes through the devices.

## Implementation
### Protocol.xml

 - Copy tables 1000000, 1000100 and 1000200
 - Copy relations
 - Copy QAction 1000000 (including code)

### Skyline.DataMiner.FlowEngineering.Protocol namespace

The `Skyline.DataMiner.FlowEngineering.Protocol` namespace contains useful classes and methods that assist developers to fill in the FLE tables in an object oriented way.

[Link to code](QAction_1/Skyline/DataMiner/FlowEngineering/Protocol)

To get access to the main 'manager' object call the `FlowEngineeringManager.GetInstance(protocol)` method. The resulting object is the main entry point of the helper classes.
```csharp
var flowEngineering = FlowEngineeringManager.GetInstance(protocol);
```

Use properties and lists to add/update/remove data in the object structure:
```csharp
flowEngineering.Interfaces.Add(new Interface("...") { ... });
flowEngineering.Interfaces["10"].AdminStatus = InterfaceAdminStatus.Up;

flowEngineering.IncomingFlows.Add(new RxFlow("...") { ... });
flowEngineering.OutgoingFlows.Add(new TxFlow("...") { ... });
...
```

After adapting properties in the objects, the tables can be updates using the following methods:
```csharp
flowEngineering.Interfaces.UpdateTable(protocol);
flowEngineering.IncomingFlows.UpdateTable(protocol);
flowEngineering.OutgoingFlows.UpdateTable(protocol);
```

> **Note**
> Data is being cached in the SLScripting process. To ensure that all old data is cleared, it's recommended to call `FlowEngineeringManagerInstances.CreateNewInstance(protocol)` after startup.

### FLE Interfaces table

The `FLE Interfaces Overview Table` lists all interfaces that are eligible for flow engineering.

Example code:
```csharp
var flowEngineering = FlowEngineeringManager.GetInstance(protocol);
var dcfInterfaceHelper = DcfInterfaceHelper.Create(protocol);
var newInterfaces = new List<Interface>();

foreach (var ifConfig in interfacesConfig)
{
	var ifIndex = Convert.ToString(ifConfig.InterfaceNumber);

	if (!flowEngineering.Interfaces.TryGetValue(ifIndex, out var intf))
		intf = new Interface(ifIndex);

	intf.Description = $"Ethernet{ifIndex}";
	intf.DisplayKey = $"Ethernet{ifIndex}";
	intf.Type = InterfaceType.Ethernet;
	intf.AdminStatus = InterfaceAdminStatus.Down;
	intf.OperationalStatus = InterfaceOperationalStatus.Down;
	intf.DcfInterfaceId = dcfInterfaceHelper.TryFindInterface(1, ifIndex, out var dcfIntf) ? dcfIntf.ID : -1;

	newInterfaces.Add(intf);
}

flowEngineering.Interfaces.ReplaceInterfaces(newInterfaces);
flowEngineering.Interfaces.UpdateTable(protocol);
```

> [!NOTE]
> Some columns are automatically calculated and don't need to be filled in:
>  - Rx/Tx Flows
>  - Rx/Tx Expected Flows
>  - Rx/Tx Expected Bitrate

The `DCF Interface ID` column is important in order to be able to follow the as-is path. To obtain the the DCF interface ID based on `parameter group` and `index`, the following helper method can be used.
It's advised to cache the `dcfInterfaceHelper` variable when being called multiple times in a loop.

```csharp
var dcfInterfaceHelper = DcfInterfaceHelper.Create(protocol);
dcfInterfaceHelper.TryFindInterface(1 /* parameter group */, interfaceIndex, out var dcfIntf);
var dcfInterfaceID = dcfIntf.ID;
```

### Incoming and Outgoing Flows table

Add as minimum:
 - IP
	- Transport Type
	- Destination IP
	- Destination Port (when available)
	- Source IP
	- Interface
 - SDI/ASI
	- Transport Type
	- Interface

Where possible also add FKs:
 - From incoming flow to outgoing flows (1-1 or 1-N relation)
 - From outgoing flow to incoming flows (N-1 relation)

Also see [example connections](#example-connections).

Example code:
```csharp
var flowEngineering = FlowEngineeringManager.GetInstance(protocol);
var newFlows = new List<Flow>();

// handle current flows
foreach (var mrnh in multicastRouteNextHops)
{
	var instance = String.Join("/", mrnh.SourceAddress, mrnh.GroupAddress, mrnh.InterfaceIndex);

	if (!flowEngineering.OutgoingFlows.TryGetValue(instance, out var flow))
	{
		// new flow
		flow = new TxFlow(instance)
		{
			TransportType = FlowTransportType.IP,
			FlowOwner = FlowOwner.LocalSystem,
			Label = mrnh.Label,
			DestinationIP = mrnh.GroupAddress,
			DestinationPort = -1,
			SourceIP = mrnh.SourceAddress,
			Interface = mrnh.InterfaceIndex,
			ForeignKeyIncoming = String.Join("/", mrnh.GroupAddress, mrnh.SourceAddress),
		};

		flowEngineering.OutgoingFlows.Add(flow);
	}

	flow.Bitrate = mrnh.BitrateActual;
	flow.IsPresent = true;

	newFlows.Add(flow);
}

// handle old flows
foreach (var flow in flowEngineering.OutgoingFlows.Values.Except(existingFlows).ToList())
{
	if (flow.FlowOwner == FlowOwner.FlowEngineering)
		flow.IsPresent = false;
	else
		flowEngineering.OutgoingFlows.Remove(flow.Instance);
}

// update tables
flowEngineering.UpdateInterfaceAndOutgoingFlowsTables(protocol);
```

### Process InterApp messages

See [QAction 9000000](QAction_9000000/QAction_9000000.cs)

```csharp
var flowEngineering = FlowEngineeringManager.GetInstance(protocol);
var (addedFlows, _) = flowEngineering.HandleInterAppMessage(protocol, Message, ignoreDestinationPort: true);

// link outgoing flows with incoming flows
foreach (var outFlow in addedFlows.OfType<TxFlow>())
{
	outFlow.ForeignKeyIncoming = $"{outFlow.SourceIP}/{outFlow.DestinationIP}";
}
```

### Flow lifecycle

```mermaid
flowchart LR
A[No row]
B[Owner=Flow Engineering<br>Present=No]
C[Owner=Flow Engineering<br>Present=Yes]
D[Owner=Local System<br>Present=Yes]

A -->|Added by FLE| B
A -->|Detected| D
B -->|Removed by FLE| A
B -->|Detected| C
C -->|Not detected| B
C -->|Removed by FLE| D
D -->|Added by FLE| C
D -->|Not detected| A
```

## Parameters

### FLE Interfaces Overview Table
> PID: 1000000

List of interfaces that are eligible for flow engineering.

| IDX | PID     | Description                | Values              | Explanation |
| --- | ------- | -------------------------- | ------------------- | ----------- |
| 0   | 1000001 | Index                      | String              | Unique key of the table row.
| 1   | 1000002 | Description                | String              | Description of the interface.
| 2   | 1000003 | Type                       | Ethernet/SDI/ASI    | Type of the interface.
| 3   | 1000004 | Admin Status               | Up/Down/Testing     | Admin status.
| 4   | 1000005 | Oper. Status               | Up/Down/Testing/... | Operational status.
| 5   | 1000006 | Display Key [IDX]          | String              | Display key.
| 6   | 1000007 | Rx Bitrate                 | Number (Mbps)       | Rx bitrate on this interface as reported by the device.
| 7   | 1000008 | Rx Flows                   | Number (Flows)      | Total number of flows in [FLE Incoming Flows Table](#fle-incoming-flows-table) that are present.
| 8   | 1000009 | Tx Bitrate                 | Number (Mbps)       | Tx bitrate on this interface as reported by the device.
| 9   | 1000010 | Tx Flows                   | Number (Flows)      | Total number of flows in [FLE Outgoing Flows Table](#fle-outgoing-flows-table) that are present.
| 10  | 1000011 | Rx Utilization             | Number (%)          | Utilization of the interface for Rx traffic.
| 11  | 1000012 | Tx Utilization             | Number (%)          | Utilization of the interface for Tx traffic.
| 12  | 1000013 | Expected Rx Bitrate        | Number (Mbps)       | Sum of all expected bitrates in [FLE Incoming Flows Table](#fle-incoming-flows-table).
| 13  | 1000014 | Expected Rx Bitrate Status | Normal/Low/High     | Status of 'Rx Bitrate' compared to 'Expected Rx Bitrate'.
| 14  | 1000015 | Expected Rx Flows          | Number (Flows)      | Total number of flows in [FLE Incoming Flows Table](#fle-incoming-flows-table).
| 15  | 1000016 | Expected Rx Flows Status   | Normal/Low/High     | Status of 'v Flows' compared to 'Expected Rx Flows'.
| 16  | 1000017 | Expected Tx Bitrate        | Number (Mbps)       | Sum of all expected bitrates in [FLE Outgoing Flows Table](#fle-outgoing-flows-table).
| 17  | 1000018 | Expected Tx Bitrate Status | Normal/Low/High     | Status of 'Tx Bitrate' compared to 'Expected Tx Bitrate'.
| 18  | 1000019 | Expected Tx Flows          | Number (Flows)      | Total number of flows in [FLE Outgoing Flows Table](#fle-outgoing-flows-table).
| 19  | 1000020 | Expected Tx Flows Status   | Normal/Low/High     | Status of 'Tx Flows' compared to 'Expected Tx Flows'.
| 20  | 1000021 | DCF Interface ID           | Number              | Link to the DCF interface in general table 65049

### FLE Incoming Flows Table
> PID: 1000100

List of all incoming flows on the device.

| IDX | PID     | Description                | Value                         | Explanation |
| --- | ------- | -------------------------- | ----------------------------- | ----------- |
| 0   | 1000101 | Instance [IDX]             | String                        | Unique key of the table row.
| 1   | 1000102 | Destination IP             | String                        | Multicast destination IP address. Empty for SDI and ASI.
| 2   | 1000103 | Destination Port           | Number                        | Multicast destination port. Empty for SDI and ASI.
| 3   | 1000104 | Source IP                  | String                        | Multicast source IP address. Empty for SDI and ASI.
| 4   | 1000105 | Incoming Interface         | String                        | Foreign key to [FLE Interfaces Overview Table](#fle-interfaces-overview-table).
| 5   | 1000106 | Transport Type             | IP/SDI/ASI                    | Transport type of the signal.
| 6   | 1000107 | Rx Bitrate                 | Number (Mbps)                 | Actual received bitrate of the flow (as reported by the device).
| 7   | 1000108 | Expected Rx Bitrate        | Number (Mbps)                 | Expected received bitrate of the flow (from FLE).
| 8   | 1000109 | Expected Rx Bitrate Status | Normal/Low/High               | Status of 'Rx Bitrate' compared to 'Expected Rx Bitrate'.
| 9   | 1000110 | Label                      | String                        | Custom label.
| 10  | 1000111 | FK Outgoing                | String                        | Foreign key to [FLE Outgoing Flows Table](#fle-outgoing-flows-table). Only use this in case of 1-N mapping between incoming and outgoing, otherwise keep empty.
| 11  | 1000112 | Linked Flow                | String (GUID)                 | GUID of the linked source flow. Empty for 'Local System' flows.
| 12  | 1000113 | Flow Owner                 | Local System/Flow Engineering | Local System: Flows that exist on the device, but not provisioned by FLE.<br>Flow Engineering: Flows that are provisioned by FLE.
| 13  | 1000114 | Present                    | No/Yes                        | Indicates if the flow is present on the system or not.

### FLE Outgoing Flows Table
> PID: 1000200

List of all outgoing flows on the device.

| IDX | PID     | Description                | Value                         | Explanation |
| --- | ------- | -------------------------- | ----------------------------- | ----------- |
| 0   | 1000201 | Instance [IDX]             | String                        | Unique key of the table row.
| 1   | 1000202 | Destination IP             | String                        | Multicast destination IP address. Empty for SDI and ASI.
| 2   | 1000203 | Destination Port           | Number                        | Multicast destination port. Empty for SDI and ASI.
| 3   | 1000204 | Source IP                  | String                        | Multicast source IP address. Empty for SDI and ASI.
| 4   | 1000205 | Incoming Interface         | String                        | Foreign key to [FLE Interfaces Overview Table](#fle-interfaces-overview-table).
| 5   | 1000206 | Transport Type             | IP/SDI/ASI                    | Transport type of the signal.
| 6   | 1000207 | Tx Bitrate                 | Number (Mbps)                 | Actual transmitted bitrate of the flow (as reported by the device).
| 7   | 1000208 | Expected Tx Bitrate        | Number (Mbps)                 | Expected transmitted bitrate of the flow (from FLE).
| 8   | 1000209 | Expected Tx Bitrate Status | Normal/Low/High               | Status of 'Tx Bitrate' compared to 'Expected Tx Bitrate'.
| 9   | 1000210 | Label                      | String                        | Custom label.
| 10  | 1000211 | FK Outgoing                | String                        | Foreign key to [FLE Incoming Flows Table](#fle-incoming-flows-table). Only use this in case of N-1 mapping between incoming and outgoing, otherwise keep empty.
| 11  | 1000212 | Linked Flow                | String (GUID)                 | GUID of the linked source flow. Empty for 'Local System' flows.
| 12  | 1000213 | Flow Owner                 | Local System/Flow Engineering | Local System: Flows that exist on the device, but not provisioned by FLE.<br>Flow Engineering: Flows that are provisioned by FLE.
| 13  | 1000214 | Present                    | No/Yes                        | Indicates if the flow is present on the system or not.

## Example connections
### IP to IP
Incoming:

| Instance | Destination IP | Source IP  | Interface | FK to Out |
| -------- | -------------- | ---------- | --------- | --------- |
| X        | 239.0.0.1      | 10.1.1.2   | Eth1      |           |

Outgoing:

| Instance | Destination IP | Source IP  | Interface | FK to In  |
| -------- | -------------- | ---------- | --------- | --------- |
| Y        | 239.0.0.1      | 10.1.1.2   | Eth2      | X         |

### IP to SDI
Incoming:

| Instance | Destination IP | Source IP  | Interface | FK to Out |
| -------- | -------------- | ---------- | --------- | --------- |
| X        | 239.0.0.1      | 10.1.1.2   | Eth1      |           |

Outgoing:

| Instance | Destination IP | Source IP  | Interface | FK to In  |
| -------- | -------------- | ---------- | --------- | --------- |
| Y        |                |            | SDI 2     | X         |

### SDI to SDI
Incoming:

| Instance | Destination IP | Source IP  | Interface | FK to Out |
| -------- | -------------- | ---------- | --------- | --------- |
| X        |                |            | SDI 1     | Y         |

Outgoing:

| Instance | Destination IP | Source IP  | Interface | FK to In  |
| -------- | -------------- | ---------- | --------- | --------- |
| Y        |                |            | SDI 2     | X         |

### SDI to IP
Incoming:

| Instance | Destination IP | Source IP  | Interface | FK to Out |
| -------- | -------------- | ---------- | --------- | --------- |
| X        |                |            | SDI 1     |           |

Outgoing:

| Instance | Destination IP | Source IP  | Interface | FK to In  |
| -------- | -------------- | ---------- | --------- | --------- |
| Y        | 239.0.0.1      | 10.1.1.2   | Eth2      | X         |

