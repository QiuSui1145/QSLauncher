import React from "react";
import { AbsoluteFill, interpolate, useCurrentFrame } from "remotion";
import { Easing } from "remotion";

/**
 * Fades a scene in and out without cutting the persistent backdrop.
 * Children receive the local frame from `useCurrentFrame()`.
 */
export const Scene: React.FC<{
  durationInFrames: number;
  children: React.ReactNode;
  fadeIn?: number;
  fadeOut?: number;
  y?: number;
}> = ({ durationInFrames, children, fadeIn = 10, fadeOut = 10, y = 24 }) => {
  const frame = useCurrentFrame();
  const opacity = interpolate(
    frame,
    [0, fadeIn, durationInFrames - fadeOut, durationInFrames],
    [0, 1, 1, 0],
    {
      extrapolateLeft: "clamp",
      extrapolateRight: "clamp",
      easing: Easing.bezier(0.16, 1, 0.3, 1),
    },
  );
  const translateY = interpolate(
    frame,
    [0, fadeIn, durationInFrames - fadeOut, durationInFrames],
    [y, 0, 0, -y * 0.5],
    {
      extrapolateLeft: "clamp",
      extrapolateRight: "clamp",
      easing: Easing.bezier(0.16, 1, 0.3, 1),
    },
  );

  return (
    <AbsoluteFill style={{ opacity, translate: `0px ${translateY}px` }}>
      {children}
    </AbsoluteFill>
  );
};
