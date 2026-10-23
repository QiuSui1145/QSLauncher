import React from "react";
import { AbsoluteFill, interpolate, useCurrentFrame, useVideoConfig } from "remotion";
import { COLORS, FONT_MONO } from "../theme";

export const Progress: React.FC<{ section: string; total: number; index: number }> = ({
  section,
  total,
  index,
}) => {
  const frame = useCurrentFrame();
  const { durationInFrames } = useVideoConfig();
  const progress = Math.min(1, frame / Math.max(1, durationInFrames - 1));
  const alpha = interpolate(frame, [0, 24], [0, 1], {
    extrapolateLeft: "clamp",
    extrapolateRight: "clamp",
  });

  return (
    <AbsoluteFill
      style={{ pointerEvents: "none", opacity: alpha, fontFamily: FONT_MONO }}
    >
      <div
        style={{
          position: "absolute",
          left: 90,
          right: 90,
          bottom: 42,
          display: "flex",
          alignItems: "center",
          gap: 18,
          color: COLORS.inkMuted,
          fontSize: 13,
          letterSpacing: "0.18em",
        }}
      >
        <span style={{ color: COLORS.green }}>QS / {section}</span>
        <div
          style={{
            flex: 1,
            height: 2,
            background: "rgba(255,255,255,0.10)",
            position: "relative",
            overflow: "hidden",
            borderRadius: 99,
          }}
        >
          <div
            style={{
              position: "absolute",
              left: 0,
              top: 0,
              bottom: 0,
              width: `${progress * 100}%`,
              background:
                "linear-gradient(90deg, rgba(110,231,168,0.4), #6ee7a8 70%, #5fd4f4)",
              boxShadow: "0 0 18px rgba(110,231,168,0.55)",
            }}
          />
        </div>
        <span>
          {String(index).padStart(2, "0")} / {String(total).padStart(2, "0")}
        </span>
      </div>
    </AbsoluteFill>
  );
};
