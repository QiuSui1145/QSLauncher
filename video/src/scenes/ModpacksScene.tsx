import React from "react";
import { AbsoluteFill, interpolate, spring, useCurrentFrame, useVideoConfig } from "remotion";
import { ShotCard } from "../components/Shot";
import { Eyebrow, Headline, Lead, Chip, Reveal } from "../components/Type";
import { COLORS, FONT, FONT_MONO } from "../theme";

export const ModpacksScene: React.FC = () => {
  const frame = useCurrentFrame();
  const { fps } = useVideoConfig();
  const fly = spring({
    frame: frame - 34,
    fps,
    config: { damping: 200, mass: 0.9, stiffness: 70 },
  });
  const dropGlow = Math.sin(frame / 16) * 0.5 + 0.5;

  return (
    <AbsoluteFill>
      <div
        style={{
          position: "absolute",
          left: 96,
          top: 104,
          width: 490,
          display: "flex",
          flexDirection: "column",
          gap: 20,
        }}
      >
        <Reveal>
          <Eyebrow color={COLORS.violet}>MODPACKS & MODS</Eyebrow>
        </Reveal>
        <Reveal delay={8}>
          <Headline size={54}>
            整合包，
            <br />
            拖进来就能装
          </Headline>
        </Reveal>
        <Reveal delay={18}>
          <Lead size={18}>
            支持 Modrinth、CurseForge、Prism 与普通 ZIP。
            选中实例，把整合包拖到窗口，确认后覆盖到当前挂载目录。
          </Lead>
        </Reveal>
        <Reveal delay={28}>
          <div style={{ display: "flex", gap: 9, flexWrap: "wrap" }}>
            <Chip tone="green">ZIP / MRPACK</Chip>
            <Chip tone="cyan">拖拽安装</Chip>
            <Chip>中文搜索</Chip>
          </div>
        </Reveal>
        <Reveal delay={38}>
          <div
            style={{
              marginTop: 6,
              padding: "16px 18px",
              borderRadius: 16,
              border: `1px solid ${COLORS.line}`,
              background: "rgba(8,16,20,0.55)",
              fontFamily: FONT,
              fontSize: 15,
              lineHeight: 1.7,
              color: COLORS.inkMuted,
              maxWidth: 430,
            }}
          >
            <span style={{ color: COLORS.green, fontFamily: FONT_MONO }}>MODRINTH</span>
            {" · "}
            官方 API
            <br />
            模组元数据、图标与版本列表全部来自官方接口。
          </div>
        </Reveal>
      </div>

      <div style={{ position: "absolute", left: 600, top: 126 }}>
        <div
          style={{
            opacity: interpolate(frame, [4, 22], [0, 1], {
              extrapolateLeft: "clamp",
              extrapolateRight: "clamp",
            }),
          }}
        >
          <ShotCard
            src="shots/modpacks.png"
            width={1240}
            tilt={-1}
            glow="rgba(154,167,255,0.20)"
            label="REAL UI · MODPACKS"
          />
        </div>
      </div>

      <div
        style={{
          position: "absolute",
          left: 76,
          top: 675,
          opacity: interpolate(frame, [16, 34], [0, 1], {
            extrapolateLeft: "clamp",
            extrapolateRight: "clamp",
          }),
          translate: `0px ${interpolate(frame, [16, 38], [34, 0], {
            extrapolateLeft: "clamp",
            extrapolateRight: "clamp",
          })}px`,
        }}
      >
        <ShotCard
          src="shots/mods.png"
          width={495}
          tilt={1.6}
          glow="rgba(74,222,128,0.18)"
          radius={16}
          label="MOD WORKBENCH"
        />
      </div>

      {/* A small drag-and-drop rehearsal over the real modpack page. */}
      <div
        style={{
          position: "absolute",
          left: 1000,
          top: 750,
          translate: `${(1 - fly) * -260}px ${(1 - fly) * 120}px`,
          opacity: interpolate(frame, [30, 42, 96, 108], [0, 1, 1, 0], {
            extrapolateLeft: "clamp",
            extrapolateRight: "clamp",
          }),
          scale: 0.9 + fly * 0.1,
        }}
      >
        <div
          style={{
            display: "flex",
            alignItems: "center",
            gap: 12,
            padding: "12px 16px",
            borderRadius: 14,
            background: "rgba(6,12,16,0.94)",
            border: `1px solid rgba(110,231,168,${0.25 + dropGlow * 0.35})`,
            boxShadow: `0 20px 60px rgba(0,0,0,0.5), 0 0 ${18 + dropGlow * 26}px rgba(74,222,128,0.20)`,
            rotate: "-2deg",
          }}
        >
          <span
            style={{
              width: 34,
              height: 34,
              borderRadius: 9,
              background: "rgba(110,231,168,0.14)",
              border: "1px solid rgba(110,231,168,0.32)",
              display: "flex",
              alignItems: "center",
              justifyContent: "center",
              color: COLORS.green,
              fontFamily: FONT_MONO,
              fontSize: 11,
            }}
          >
            ZIP
          </span>
          <div style={{ display: "flex", flexDirection: "column", gap: 3 }}>
            <span
              style={{
                fontFamily: FONT,
                fontSize: 15,
                color: COLORS.ink,
              }}
            >
              落幕曲 1.6.7.1.zip
            </span>
            <span
              style={{
                fontFamily: FONT_MONO,
                fontSize: 10,
                letterSpacing: "0.08em",
                color: COLORS.inkMuted,
              }}
            >
              松开以安装到当前实例
            </span>
          </div>
        </div>
      </div>

      <div
        style={{
          position: "absolute",
          right: 100,
          bottom: 58,
          fontFamily: FONT_MONO,
          fontSize: 12,
          letterSpacing: "0.14em",
          color: COLORS.inkMuted,
        }}
      >
        DRAG · DROP · OVERLAY
      </div>
    </AbsoluteFill>
  );
};
