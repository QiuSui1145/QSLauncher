import React from "react";
import { Easing, Img, interpolate, staticFile, useCurrentFrame } from "remotion";
import { COLORS, FONT_MONO } from "../theme";

export const ShotCard: React.FC<{
  src: string;
  width: number;
  style?: React.CSSProperties;
  tilt?: number;
  glow?: string;
  label?: string;
  kenBurns?: boolean;
  radius?: number;
  aspect?: string;
}> = ({
  src,
  width,
  style,
  tilt = 0,
  glow = "rgba(74,222,128,0.18)",
  label,
  kenBurns = true,
  radius = 20,
  aspect = "1623 / 1031",
}) => {
  const frame = useCurrentFrame();
  const zoom = kenBurns
    ? interpolate(frame, [0, 260], [1.045, 1.0], {
        extrapolateLeft: "clamp",
        extrapolateRight: "clamp",
        easing: Easing.bezier(0.16, 1, 0.3, 1),
      })
    : 1;

  return (
    <div
      style={{
        width,
        aspectRatio: aspect,
        borderRadius: radius,
        position: "relative",
        boxShadow: `0 50px 120px rgba(0,0,0,0.55), 0 0 0 1px rgba(255,255,255,0.10), 0 0 90px ${glow}`,
        rotate: `${tilt}deg`,
        ...style,
      }}
    >
      <div
        style={{
          position: "absolute",
          inset: 0,
          borderRadius: radius,
          overflow: "hidden",
          background: "#071015",
        }}
      >
        <Img
          src={staticFile(src)}
          style={{
            width: "100%",
            height: "100%",
            objectFit: "cover",
            display: "block",
            scale: zoom,
          }}
        />
        <div
          style={{
            position: "absolute",
            inset: 0,
            background:
              "linear-gradient(180deg, rgba(255,255,255,0.10) 0%, transparent 16%, transparent 78%, rgba(0,0,0,0.24) 100%)",
            pointerEvents: "none",
          }}
        />
      </div>
      <div
        style={{
          position: "absolute",
          inset: 0,
          borderRadius: radius,
          border: "1px solid rgba(255,255,255,0.14)",
          pointerEvents: "none",
        }}
      />
      {label ? (
        <div
          style={{
            position: "absolute",
            left: 18,
            bottom: -18,
            padding: "8px 14px",
            borderRadius: 999,
            background: "rgba(7,14,18,0.92)",
            border: "1px solid rgba(255,255,255,0.12)",
            color: COLORS.inkSoft,
            fontFamily: FONT_MONO,
            fontSize: 14,
            letterSpacing: "0.08em",
            whiteSpace: "nowrap",
          }}
        >
          {label}
        </div>
      ) : null}
    </div>
  );
};

export const Callout: React.FC<{
  index: string;
  title: string;
  text?: string;
  style?: React.CSSProperties;
  delay?: number;
  align?: "left" | "right";
  tone?: string;
}> = ({ index, title, text, style, delay = 0, align = "left", tone = COLORS.green }) => {
  const frame = useCurrentFrame();
  const progress = interpolate(frame - delay, [0, 18], [0, 1], {
    extrapolateLeft: "clamp",
    extrapolateRight: "clamp",
    easing: Easing.bezier(0.16, 1, 0.3, 1),
  });
  const pulse = 0.5 + Math.sin((frame + delay) / 18) * 0.5;

  return (
    <div
      style={{
        display: "flex",
        alignItems: "flex-start",
        gap: 14,
        opacity: progress,
        translate: `${(1 - progress) * (align === "left" ? -24 : 24)}px 0px`,
        ...style,
      }}
    >
      <div
        style={{
          width: 34,
          height: 34,
          borderRadius: 11,
          display: "flex",
          alignItems: "center",
          justifyContent: "center",
          background: "rgba(8,16,20,0.9)",
          border: `1px solid ${tone}55`,
          boxShadow: `0 0 ${12 + pulse * 14}px ${tone}33`,
          color: tone,
          fontFamily: FONT_MONO,
          fontSize: 15,
          fontWeight: 700,
          flexShrink: 0,
        }}
      >
        {index}
      </div>
      <div style={{ display: "flex", flexDirection: "column", gap: 5 }}>
        <span
          style={{
            fontFamily: '"Segoe UI", "Microsoft YaHei UI", sans-serif',
            fontSize: 22,
            fontWeight: 650,
            color: COLORS.ink,
            letterSpacing: "-0.01em",
          }}
        >
          {title}
        </span>
        {text ? (
          <span
            style={{
              fontFamily: '"Segoe UI", "Microsoft YaHei UI", sans-serif',
              fontSize: 16,
              lineHeight: 1.55,
              color: COLORS.inkMuted,
              maxWidth: 330,
            }}
          >
            {text}
          </span>
        ) : null}
      </div>
    </div>
  );
};
