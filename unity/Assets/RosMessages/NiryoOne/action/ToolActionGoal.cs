using System.Collections.Generic;
using Unity.Robotics.ROSTCPConnector.MessageGeneration;
using RosMessageTypes.Std;
using RosMessageTypes.Actionlib;

namespace RosMessageTypes.NiryoOne
{
    public class ToolActionGoal : ActionGoal<ToolGoal>
    {
        public const string k_RosMessageName = "niryo_one_msgs/ToolActionGoal";
        public override string RosMessageName => k_RosMessageName;


        public ToolActionGoal() : base()
        {
            this.goal = new ToolGoal();
        }

        public ToolActionGoal(HeaderMsg header, GoalIDMsg goal_id, ToolGoal goal) : base(header, goal_id)
        {
            this.goal = goal;
        }
        public static ToolActionGoal Deserialize(MessageDeserializer deserializer) => new ToolActionGoal(deserializer);

        ToolActionGoal(MessageDeserializer deserializer) : base(deserializer)
        {
            this.goal = ToolGoal.Deserialize(deserializer);
        }
        public override void SerializeTo(MessageSerializer serializer)
        {
            serializer.Write(this.header);
            serializer.Write(this.goal_id);
            serializer.Write(this.goal);
        }


#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#else
        [UnityEngine.RuntimeInitializeOnLoadMethod]
#endif
        public static void Register()
        {
            MessageRegistry.Register(k_RosMessageName, Deserialize);
        }
    }
}
