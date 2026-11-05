using System.Collections.Generic;
using Unity.Robotics.ROSTCPConnector.MessageGeneration;
using RosMessageTypes.Std;
using RosMessageTypes.Actionlib;

namespace RosMessageTypes.NiryoOne
{
    public class RobotMoveActionGoal : ActionGoal<RobotMoveGoal>
    {
        public const string k_RosMessageName = "niryo_one_msgs/RobotMoveActionGoal";
        public override string RosMessageName => k_RosMessageName;


        public RobotMoveActionGoal() : base()
        {
            this.goal = new RobotMoveGoal();
        }

        public RobotMoveActionGoal(HeaderMsg header, GoalIDMsg goal_id, RobotMoveGoal goal) : base(header, goal_id)
        {
            this.goal = goal;
        }
        public static RobotMoveActionGoal Deserialize(MessageDeserializer deserializer) => new RobotMoveActionGoal(deserializer);

        RobotMoveActionGoal(MessageDeserializer deserializer) : base(deserializer)
        {
            this.goal = RobotMoveGoal.Deserialize(deserializer);
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
