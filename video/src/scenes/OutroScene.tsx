import React from "react";
import { AbsoluteFill, Img, interpolate, staticFile, useCurrentFrame } from "remotion";
import { Eyebrow, Headline, Chip, Reveal } from "../components/Type";
import { Logo } from "../components/Logo";
import { COLORS, FONT, FONT_MONO } from "../theme";

export const OutroScene: React.FC = () => {
  const frame = useCurrentFrame();

  return (
    <AbsoluteFill>
      <div
        style={{
          position: "absolute",
          left: -180,
          top: 230,
          width: 900,
          opacity: 0.14,
          rotate: "-8deg",
          filter: "blur(1px)",
        }}
      >
        <Img src={staticFile("shots/home.png")} style={{ width: "100%" }} />
      </div>
      <div
        style={{
          position: "absolute",
          right: -200,
          top: 260,
          width: 900,
          opacity: 0.12,
          rotate: "7deg",
          filter: "blur(1px)",
        }}
      >
        <Img src={staticFile("shots/settings.png")} style={{ width: "100%" }} />
      </div>

      <div
        style={{
          position: "absolute",
          inset: 0,
          display: "flex",
          flexDirection: "column",
          alignItems: "center",
          justifyContent: "center",
          gap: 30,
        }}
      >
        <Reveal>
          <Logo size={64} />
        </Reveal>
        <Reveal delay={8}>
          <Eyebrow>QS LAUNCHER 1.2.0</Eyebrow>
        </Reveal>
        <Reveal delay={16}>
          <Headline size={92} style={{ textAlign: "center", lineHeight: 1.14 }}>
            去中心化 · 官方源
            <br />
            <span
              style={{
                background:
                  "linear-gradient(100deg, #f2f7f8 5%, #6ee7a8 52%, #5fd4f4 96%)",
                WebkitBackgroundClip: "text",
                color: "transparent",
              }}
            >
              完全便携
            </span>
          </Headline>
        </Reveal>

        <Reveal delay={28}>
          <div style={{ display: "flex", gap: 12 }}>
            <Chip tone="green">Windows</Chip>
            <Chip tone="cyan">单文件</Chip>
            <Chip tone="violet">免安装</Chip>
          </div>
        </Reveal>

        <Reveal delay={40}>
          <div
            style={{
              marginTop: 18,
              display: "flex",
              flexDirection: "column",
              alignItems: "center",
              gap: 8,
              fontFamily: FONT_MONO,
              fontSize: 15,
              letterSpacing: "0.08em",
              color: COLORS.inkMuted,
            }}
          >
            <span>QSLauncher-1.2.0-win-x64.exe</span>
            <span style={{ opacity: 0.55 }}>QSLauncher-1.2.0-lite.exe</span>
          </div>
        </Reveal>
      </div>

      <div
        style={{
          position: "absolute",
          left: 0,
          right: 0,
          bottom: 82,
          textAlign: "center",
          fontFamily: FONT,
          fontSize: 13,
          color: COLORS.inkMuted,
          opacity: interpolate(frame, [50, 70], [0, 0.85], {
            extrapolateLeft: "clamp",
            extrapolateRight: "clamp",
          }),
        }}
      >
        实机演示使用独立 data 目录，不含任何用户数据 · 所有下载均指向官方源
      </div>
    </AbsoluteFill>
  );
};
