using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;

namespace MotionMatching
{
    /// <summary>
    /// Inertialization is a type of blending between two poses.
    /// Typically to blend between two poses, we use crossfade. Both poses are stored queried during the transition
    /// and they are blended during the transition.
    /// The basic idea of inertialization is when we change the pose we change it for real but compute offsets that
    /// takes us from the new/target pose to the source one. Then, we decay this offset using a polynomial function or springs.
    /// Decaying the offset will progressively take us to the target pose.
    /// </summary>
    public class Inertialization
    {
        public quaternion[] InertializedRotations;
        public float3[] InertializedAngularVelocities;
        public float3 InertializedHips;
        public float3 InertializedHipsVelocity;

        private quaternion[] OffsetRotations;
        private float3[] OffsetAngularVelocities;
        private float3 OffsetHips;
        private float3 OffsetHipsVelocity;

        public Inertialization(Skeleton skeleton)
        {
            int numJoints = skeleton.Joints.Count;
            InertializedRotations = new quaternion[numJoints];
            InertializedAngularVelocities = new float3[numJoints];
            OffsetRotations = new quaternion[numJoints];
            for (int i = 0; i < numJoints; i++) OffsetRotations[i] = quaternion.identity; // init to a valid quaternion
            OffsetAngularVelocities = new float3[numJoints];
        }

        /// <summary>
        /// It takes as input the current state of the source pose and the target pose.
        /// It sets up the inertialization, which can then by updated by calling InertializeUpdate(...).
        /// </summary>
        public void PoseTransition(PoseSet poseSet, int sourcePoseIndex, int targetPoseIndex)
        {
            poseSet.GetPose(sourcePoseIndex, out PoseVector sourcePose);
            poseSet.GetPose(targetPoseIndex, out PoseVector targetPose);
            // Set up the inertialization for joint local rotations (no simulation bone)
            for (int i = 1; i < sourcePose.JointLocalRotations.Length; i++)
            {
                quaternion sourceJointRotation = sourcePose.JointLocalRotations[i];
                quaternion targetJointRotation = targetPose.JointLocalRotations[i];
                float3 sourceJointAngularVelocity = sourcePose.JointAngularVelocities[i];
                float3 targetJointAngularVelocity = targetPose.JointAngularVelocities[i];
                InertializeJointTransition(sourceJointRotation, sourceJointAngularVelocity,
                                           targetJointRotation, targetJointAngularVelocity,
                                           ref OffsetRotations[i], ref OffsetAngularVelocities[i]);
            }
            // Set up the inertialization for root Y
            float3 sourceHips = sourcePose.JointLocalPositions[1];
            float3 targetHips = targetPose.JointLocalPositions[1];
            float3 sourceHipsVelocity = sourcePose.JointVelocities[1];
            float3 targetHipsVelocity = targetPose.JointVelocities[1];
            InertializeJointTransition(sourceHips, sourceHipsVelocity,
                                       targetHips, targetHipsVelocity,
                                       ref OffsetHips, ref OffsetHipsVelocity);
        }

        /// <summary>
        /// Updates the inertialization decaying the offset from the source pose (specified in InertializePoseTransition(...))
        /// to the target pose.
        /// </summary>
        public void Update(PoseVector targetPose, float halfLife, float deltaTime)
        {
            // Update the inertialization for joint local rotations
            for (int i = 1; i < targetPose.JointLocalRotations.Length; i++)
            {
                quaternion targetJointRotation = targetPose.JointLocalRotations[i];
                float3 targetAngularVelocity = targetPose.JointAngularVelocities[i];
                InertializeJointUpdate(targetJointRotation, targetAngularVelocity,
                                       halfLife, deltaTime,
                                       ref OffsetRotations[i], ref OffsetAngularVelocities[i],
                                       out InertializedRotations[i], out InertializedAngularVelocities[i]);
            }
            // Update the inertialization for root Y
            float3 targetHips = targetPose.JointLocalPositions[1];
            float3 targetRootVelocity = targetPose.JointVelocities[1];
            InertializeJointUpdate(targetHips, targetRootVelocity,
                                   halfLife, deltaTime,
                                   ref OffsetHips, ref OffsetHipsVelocity,
                                   out InertializedHips, out InertializedHipsVelocity);
        }

        /// <summary>
        /// Compute the offsets from the source pose to the target pose.
        /// Offsets are in/out since we may start a inertialization in the middle of another inertialization.
        /// </summary>
        private static void InertializeJointTransition(quaternion sourceRot, float3 sourceAngularVel,
                                                       quaternion targetRot, float3 targetAngularVel,
                                                       ref quaternion offsetRot, ref float3 offsetAngularVel)
        {
            offsetRot = math.normalizesafe(MathExtensions.Abs(math.mul(math.inverse(targetRot), math.mul(sourceRot, offsetRot))));
            offsetAngularVel = (sourceAngularVel + offsetAngularVel) - targetAngularVel;
        }
        /// <summary>
        /// Compute the offsets from the source pose to the target pose.
        /// Offsets are in/out since we may start a inertialization in the middle of another inertialization.
        /// </summary>
        private static void InertializeJointTransition(float3 source, float3 sourceVel,
                                                       float3 target, float3 targetVel,
                                                       ref float3 offset, ref float3 offsetVel)
        {
            offset = (source + offset) - target;
            offsetVel = (sourceVel + offsetVel) - targetVel;
        }
        /// <summary>
        /// Compute the offsets from the source pose to the target pose.
        /// Offsets are in/out since we may start a inertialization in the middle of another inertialization.
        /// </summary>
        private static void InertializeJointTransition(float source, float sourceVel,
                                                       float target, float targetVel,
                                                       ref float offset, ref float offsetVel)
        {
            offset = (source + offset) - target;
            offsetVel = (sourceVel + offsetVel) - targetVel;
        }

        /// <summary>
        /// Updates the inertialization decaying the offset and applying it to the target pose
        /// </summary>
        private static void InertializeJointUpdate(quaternion targetRot, float3 targetAngularVel,
                                                   float halfLife, float deltaTime,
                                                   ref quaternion offsetRot, ref float3 offsetAngularVel,
                                                   out quaternion newRot, out float3 newAngularVel)
        {
            Spring.DecaySpringDamperImplicit(ref offsetRot, ref offsetAngularVel, halfLife, deltaTime);
            newRot = math.mul(targetRot, offsetRot);
            newAngularVel = targetAngularVel + offsetAngularVel;
        }
        /// <summary>
        /// Updates the inertialization decaying the offset and applying it to the target pose
        /// </summary>
        private static void InertializeJointUpdate(float3 target, float3 targetVel,
                                                   float halfLife, float deltaTime,
                                                   ref float3 offset, ref float3 offsetVel,
                                                   out float3 newValue, out float3 newVel)
        {
            Spring.DecaySpringDamperImplicit(ref offset, ref offsetVel, halfLife, deltaTime);
            newValue = target + offset;
            newVel = targetVel + offsetVel;
        }
        /// <summary>
        /// Updates the inertialization decaying the offset and applying it to the target pose
        /// </summary>
        private static void InertializeJointUpdate(float target, float targetVel,
                                                   float halfLife, float deltaTime,
                                                   ref float offset, ref float offsetVel,
                                                   out float newValue, out float newVel)
        {
            Spring.DecaySpringDamperImplicit(ref offset, ref offsetVel, halfLife, deltaTime);
            newValue = target + offset;
            newVel = targetVel + offsetVel;
        }
    }
}