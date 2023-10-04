﻿namespace QAction_9000000
{
	using Skyline.DataMiner.CommunityLibrary.FlowProvisioning.Info;
	using Skyline.DataMiner.FlowEngineering.Protocol;
	using Skyline.DataMiner.Library.Common.InterAppCalls.CallSingle;
	using Skyline.DataMiner.Library.Common.InterAppCalls.MessageExecution;
	using Skyline.DataMiner.Scripting;

	public class FlowProvisioningExecutor : SimpleMessageExecutor<FlowInfoMessage>
	{
		public FlowProvisioningExecutor(FlowInfoMessage message) : base(message)
		{
			// Nothing to do
		}

		public override bool TryExecute(object dataSource, object dataDestination, out Message optionalReturnMessage)
		{
			var protocol = (SLProtocolExt)dataDestination;

			// Flow engineering
			var flowEngineering = FlowEngineeringManager.GetInstance(protocol);
			flowEngineering.HandleInterAppMessage(protocol, Message);

			optionalReturnMessage = null;
			return true;
		}
	}
}