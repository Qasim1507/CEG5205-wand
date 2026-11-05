using System.Collections.Generic;
using Unity.Robotics.ROSTCPConnector.MessageGeneration;
using RosMessageTypes.Std;
using RosMessageTypes.Actionlib;

namespace RosMessageTypes.NiryoOne
{
    public class SequenceActionGoal : ActionGoal<SequenceGoal>
    {
        public const string k_RosMessageName = "niryo_one_msgs/SequenceActionGoal";
        public override string RosMessageName => k_RosMessageName;


        public SequenceActionGoal() : base()
        {
            this.goal = new SequenceGoal();
        }

        public SequenceActionGoal(HeaderMsg header, GoalIDMsg goal_id, SequenceGoal goal) : base(header, goal_id)
        {
            this.goal = goal;
        }
        public static SequenceActionGoal Deserialize(MessageDeserializer deserializer) => new SequenceActionGoal(deserializer);

        SequenceActionGoal(MessageDeserializer deserializer) : base(deserializer)
        {
            this.goal = SequenceGoal.Deserialize(deserializer);
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
