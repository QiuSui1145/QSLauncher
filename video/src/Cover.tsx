import React from "react";
import { AbsoluteFill } from "remotion";
import { Backdrop } from "./components/Backdrop";
import { ShotCard } from "./components/Shot";
import { Chip, Eyebrow, Headline, Lead } from "./components/Type";
import { Logo } from "./components/Logo";
import { COLORS, FONT, FONT_MONO } from "./theme";

/** Static 16:9 cover for video platforms. No timeline-dependent entrance. */
export const QSLauncherCover: React.FC = () => {
  return (
    <AbsoluteFill style={{ fontFamily: FONT }}>
      <Backdrop />

      <div style={{ position: "absolute", left: 90, top: 64 }}>
        <Logo size={58} withWordmark />
      </div>

      <div
        style={{
          position: "absolute",
          left: 92,
          top: 240,
          width: 720,
          display: "flex",
          flexDirection: "column",
          gap: 26,
        }}
      >
        <Eyebrow>MINECRAFT JAVA · 1.2.0</Eyebrow>
        <Headline size={94} style={{ lineHeight: 1.08 }}>
          把 Minecraft
          <br />
          <span
            style={{
              background:
                "linear-gradient(100deg, #f2f7f8 10%, #6ee7a8 55%, #5fd4f4 95%)",
              WebkitBackgroundClip: "text",
              color: "transparent",
            }}
          >
            交还给你
          </span>
        </Headline>
        <Lead size={23}>
          一个完全便携、去中心化的启动器。
          <br />
          不代理下载，不收集数据，所有下载直连官方源。
        </Lead>
        <div style={{ display: "flex", gap: 12 }}>
          <Chip tone="green">完全便携</Chip>
          <Chip tone="cyan">去中心化</Chip>
          <Chip tone="violet">官方源</Chip>
        </div>
      </div>

      <div style={{ position: "absolute", left: 870, top: 170 }}>
        <ShotCard
          src="shots/home.png"
          width={1030}
          tilt={-2}
          kenBurns={false}
          glow="rgba(74,222,128,0.26)"
        />
      </div>

      <div
        style={{
          position: "absolute",
          left: 92,
          bottom: 54,
          fontFamily: FONT_MONO,
          fontSize: 13,
          letterSpacing: "0.16em",
          color: COLORS.inkMuted,
        }}
      >
        WINDOWS · 单文件便携版 · 实机演示
      </div>
    </AbsoluteFill>
  );
};
