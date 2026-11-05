#!/usr/bin/env python3
"""Local RobotMove action server for testing Part 4 without Niryo hardware.

The normal Part 4 launch expects this action server to run on the real robot.
Actionlib publishes the same goal messages that Unity's simulated arm consumes.
"""

import actionlib
import rospy
from niryo_one_msgs.msg import RobotMoveAction, RobotMoveResult


class SimRobotActionServer:
    def __init__(self):
        self.server = actionlib.SimpleActionServer(
            "niryo_one/commander/robot_action",
            RobotMoveAction,
            execute_cb=self.execute,
            auto_start=False,
        )
        self.server.start()
        rospy.loginfo("Part 4 simulated RobotMove action server ready")

    def execute(self, goal):
        command = goal.cmd
        if command.cmd_type == 7:
            points = command.Trajectory.trajectory.joint_trajectory.points
            duration = max(2.0, len(points) * 0.12 + 2.0)
        elif command.cmd_type == 6:
            duration = 1.5
        else:
            self.server.set_aborted(RobotMoveResult(status=0, message="Unsupported command"))
            return

        finish = rospy.Time.now() + rospy.Duration(duration)
        rate = rospy.Rate(20)
        while not rospy.is_shutdown() and rospy.Time.now() < finish:
            if self.server.is_preempt_requested():
                self.server.set_preempted()
                return
            rate.sleep()
        self.server.set_succeeded(RobotMoveResult(status=1, message="Simulated command complete"))


if __name__ == "__main__":
    rospy.init_node("sim_robot_action_server")
    SimRobotActionServer()
    rospy.spin()
