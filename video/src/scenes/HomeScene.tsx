import React from "react";
import { AbsoluteFill, interpolate, useCurrentFrame } from "remotion";
import { Callout, ShotCard } from "../components/Shot";
import { Eyebrow, Headline, Lead, Chip, Reveal } from "../components/Type";
import { COLORS, FONT_MONO } from "../theme";

export const HomeScene: React.FC = () => {
  const frame = useCurrentFrame();

  return (
    <AbsoluteFill>
      <div
        style={{
          position: "absolute",
          left: 96,
          top: 112,
          width: 430,
          display: "flex",
          flexDirection: "column",
          gap: 22,
        }}
      >
        <Reveal>
          <Eyebrow>WORKSPACE</Eyebrow>
        </Reveal>
        <Reveal delay={8}>
          <Headline size={58}>
            打开就是
            <br />
            你的工作台
          </Headline>
        </Reveal>
        <Reveal delay={18}>
          <Lead size={19}>
            首页把启动、实例、模组与磁盘用量放在同一屏。
            控件可以拖动、缩放、隐藏，布局跟着你的习惯走。
          </Lead>
        </Reveal>
        <Reveal delay={28}>
          <div style={{ display: "flex", gap: 10, flexWrap: "wrap" }}>
            <Chip tone="green">可拖拽布局</Chip>
            <Chip>智能内存</Chip>
          </div>
        </Reveal>
      </div>

      <div
        style={{
          position: "absolute",
          left: 96,
          top: 552,
          display: "flex",
          flexDirection: "column",
          gap: 34,
        }}
      >
        {[
          {
            index: "01",
            title: "一屏启动",
            text: "当前实例、版本、加载器与上次启动时间一目了然。",
          },
          {
            index: "02",
            title: "智能内存",
            text: "按模组数量与本机可用内存，自动给出更激进的堆大小。",
          },
          {
            index: "03",
            title: "最近实例",
            text: "常用实例直接回到眼前，不必再翻版本列表。",
          },
        ].map((item, i) => (
          <div key={item.index} style={{ display: "flex", alignItems: "center", gap: 14 }}>
            <Callout
              index={item.index}
              title={item.title}
              text={item.text}
              delay={38 + i * 10}
              style={{ width: 360 }}
            />
            <div
              style={{
                width: 62,
                height: 1,
                background: `linear-gradient(90deg, rgba(110,231,168,0.55), transparent)`,
                opacity: interpolate(frame, [40 + i * 10, 58 + i * 10], [0, 1], {
                  extrapolateLeft: "clamp",
                  extrapolateRight: "clamp",
                }),
              }}
            />
          </div>
        ))}
      </div>

      <div
        style={{
          position: "absolute",
          left: 560,
          top: 128,
          opacity: interpolate(frame, [4, 22], [0, 1], {
            extrapolateLeft: "clamp",
            extrapolateRight: "clamp",
          }),
          translate: `${interpolate(frame, [4, 26], [50, 0], {
            extrapolateLeft: "clamp",
            extrapolateRight: "clamp",
          })}px 0px`,
        }}
      >
        <ShotCard
          src="shots/home.png"
          width={1290}
          tilt={-1.4}
          glow="rgba(56,189,248,0.18)"
          label="REAL UI · HOME"
        />
      </div>

      <div
        style={{
          position: "absolute",
          left: 600,
          bottom: 62,
          fontFamily: FONT_MONO,
          fontSize: 12,
          letterSpacing: "0.14em",
          color: COLORS.inkMuted,
        }}
      >
        DRAG · RESIZE · HIDE WIDGETS
      </div>
    </AbsoluteFill>
  );
};
