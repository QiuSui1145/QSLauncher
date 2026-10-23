import React from "react";
import { AbsoluteFill, interpolate, Sequence, spring, useCurrentFrame, useVideoConfig } from "remotion";
import { Backdrop } from "./components/Backdrop";
import { Progress } from "./components/Progress";
import { Scene } from "./components/Scene";
import { ShotCard } from "./components/Shot";
import { Logo } from "./components/Logo";
import {
  Chip,
  Eyebrow,
  GlassPanel,
  Headline,
  Lead,
  Reveal,
  Stat,
} from "./components/Type";
import { COLORS, FONT, FONT_MONO } from "./theme";

const Intro120: React.FC = () => {
  return (
    <AbsoluteFill>
      <div
        style={{
          position: "absolute",
          inset: 0,
          display: "flex",
          flexDirection: "column",
          alignItems: "center",
          justifyContent: "center",
          gap: 26,
        }}
      >
        <Reveal>
          <Logo size={62} />
        </Reveal>
        <Reveal delay={6}>
          <Eyebrow>UPDATE · 1.2.0</Eyebrow>
        </Reveal>
        <Reveal delay={14}>
          <Headline size={96} style={{ textAlign: "center" }}>
            更聪明，更干净
            <br />
            <span style={{ color: COLORS.green }}>更顺手</span>
          </Headline>
        </Reveal>
        <Reveal delay={26}>
          <Lead size={23} style={{ textAlign: "center", maxWidth: 720 }}>
            智能内存 · 磨砂界面 · ZIP 整合包拖拽安装
          </Lead>
        </Reveal>
      </div>
    </AbsoluteFill>
  );
};

const Memory120: React.FC = () => {
  const frame = useCurrentFrame();
  const value = Math.round(
    interpolate(frame, [26, 96], [4096, 12288], {
      extrapolateLeft: "clamp",
      extrapolateRight: "clamp",
    }) / 128,
  ) * 128;
  const pct = value / 32768;

  return (
    <AbsoluteFill>
      <div
        style={{
          position: "absolute",
          left: 96,
          top: 132,
          width: 570,
          display: "flex",
          flexDirection: "column",
          gap: 22,
        }}
      >
        <Reveal>
          <Eyebrow>SMART MEMORY</Eyebrow>
        </Reveal>
        <Reveal delay={8}>
          <Headline size={58}>内存不再保守</Headline>
        </Reveal>
        <Reveal delay={16}>
          <Lead size={19}>
            直接读取本机当前可用物理内存。可用内存充足时，自动分配更大的 Java 堆，而不是固定 4 GB。
          </Lead>
        </Reveal>

        <Reveal delay={26}>
          <GlassPanel style={{ padding: "26px 28px", width: 520 }}>
            <div style={{ display: "flex", justifyContent: "space-between", alignItems: "flex-end" }}>
              <Stat value={`${Math.round(value / 1024)} GB`} label="自动推荐堆大小" />
              <span
                style={{
                  fontFamily: FONT_MONO,
                  fontSize: 12,
                  letterSpacing: "0.12em",
                  color: COLORS.green,
                }}
              >
                AVAILABLE-AWARE
              </span>
            </div>
            <div
              style={{
                marginTop: 22,
                height: 9,
                borderRadius: 99,
                background: "rgba(255,255,255,0.08)",
                overflow: "hidden",
              }}
            >
              <div
                style={{
                  width: `${pct * 100}%`,
                  height: "100%",
                  borderRadius: 99,
                  background: "linear-gradient(90deg, #22c98a, #6ee7a8, #5fd4f4)",
                  boxShadow: "0 0 20px rgba(110,231,168,0.5)",
                }}
              />
            </div>
            <div
              style={{
                marginTop: 12,
                display: "flex",
                justifyContent: "space-between",
                fontFamily: FONT_MONO,
                fontSize: 11,
                color: COLORS.inkMuted,
              }}
            >
              <span>2 GB</span>
              <span>32 GB 物理内存</span>
            </div>
          </GlassPanel>
        </Reveal>
      </div>

      <div style={{ position: "absolute", left: 700, top: 138 }}>
        <ShotCard
          src="shots/home.png"
          width={1150}
          tilt={-1.4}
          glow="rgba(74,222,128,0.22)"
          label="AUTO MEMORY"
        />
      </div>
    </AbsoluteFill>
  );
};

const Frost120: React.FC = () => {
  const frame = useCurrentFrame();
  const highlight = interpolate(frame, [30, 52], [0, 1], {
    extrapolateLeft: "clamp",
    extrapolateRight: "clamp",
  });

  return (
    <AbsoluteFill>
      <div
        style={{
          position: "absolute",
          left: 96,
          top: 118,
          width: 590,
          display: "flex",
          flexDirection: "column",
          gap: 20,
        }}
      >
        <Reveal>
          <Eyebrow color={COLORS.cyan}>FROSTED UI</Eyebrow>
        </Reveal>
        <Reveal delay={8}>
          <Headline size={56}>菜单不再是白块</Headline>
        </Reveal>
        <Reveal delay={16}>
          <Lead size={18} style={{ maxWidth: 540 }}>
            账户菜单、下拉框与次级按钮改为半透明磨砂材质。面板颗粒始终比菜单高 10%，让文字对比更清楚。
          </Lead>
        </Reveal>

        <Reveal delay={28}>
          <div style={{ display: "flex", flexDirection: "column", gap: 10, width: 500 }}>
            <div
              style={{
                padding: "14px 18px",
                borderRadius: 13,
                border: "1px solid rgba(255,255,255,0.10)",
                background: "rgba(255,255,255,0.04)",
                display: "flex",
                justifyContent: "space-between",
                color: COLORS.inkMuted,
                fontFamily: FONT,
                fontSize: 16,
              }}
            >
              <span>旧版：实心白底菜单</span>
              <span style={{ fontFamily: FONT_MONO, fontSize: 12 }}>OPAQUE</span>
            </div>
            <div
              style={{
                padding: "14px 18px",
                borderRadius: 13,
                border: `1px solid rgba(110,231,168,${0.2 + highlight * 0.4})`,
                background: `rgba(110,231,168,${0.05 + highlight * 0.10})`,
                display: "flex",
                justifyContent: "space-between",
                color: COLORS.ink,
                fontFamily: FONT,
                fontSize: 16,
                boxShadow: `0 0 ${highlight * 28}px rgba(74,222,128,0.18)`,
              }}
            >
              <span>新版：半透明磨砂 + 噪点</span>
              <span style={{ fontFamily: FONT_MONO, fontSize: 12, color: COLORS.green }}>
                FROSTED
              </span>
            </div>
          </div>
        </Reveal>

        <Reveal delay={44}>
          <div style={{ display: "flex", gap: 9 }}>
            <Chip tone="green">悬停反馈</Chip>
            <Chip tone="cyan">磨砂强度可调</Chip>
          </div>
        </Reveal>
      </div>

      <div style={{ position: "absolute", left: 680, top: 104 }}>
        <ShotCard
          src="shots/settings.png"
          width={1160}
          tilt={-1}
          glow="rgba(95,212,244,0.20)"
          label="FROSTED SETTINGS"
        />
      </div>

      <div
        style={{
          position: "absolute",
          left: 1370,
          top: 610,
          opacity: interpolate(frame, [22, 42], [0, 1], {
            extrapolateLeft: "clamp",
            extrapolateRight: "clamp",
          }),
          translate: `0px ${interpolate(frame, [22, 44], [36, 0], {
            extrapolateLeft: "clamp",
            extrapolateRight: "clamp",
          })}px`,
        }}
      >
        <ShotCard
          src="shots/account-menu.png"
          width={500}
          aspect="780 / 400"
          tilt={1.7}
          glow="rgba(74,222,128,0.20)"
          radius={16}
          label="FROSTED MENU"
        />
      </div>
    </AbsoluteFill>
  );
};

const Modpack120: React.FC = () => {
  const frame = useCurrentFrame();
  const { fps } = useVideoConfig();
  const fly = spring({
    frame: frame - 30,
    fps,
    config: { damping: 200, mass: 0.9, stiffness: 70 },
  });

  return (
    <AbsoluteFill>
      <div
        style={{
          position: "absolute",
          left: 96,
          top: 112,
          width: 560,
          display: "flex",
          flexDirection: "column",
          gap: 20,
        }}
      >
        <Reveal>
          <Eyebrow color={COLORS.violet}>ZIP MODPACKS</Eyebrow>
        </Reveal>
        <Reveal delay={8}>
          <Headline size={56}>
            整合包，
            <br />
            直接拖进窗口
          </Headline>
        </Reveal>
        <Reveal delay={16}>
          <Lead size={18}>
            自动识别 Modrinth、CurseForge、Prism 与普通 ZIP。松开鼠标并确认后，整合包会覆盖到当前实例的挂载目录。
          </Lead>
        </Reveal>
        <Reveal delay={28}>
          <div style={{ display: "flex", gap: 9, flexWrap: "wrap" }}>
            <Chip tone="green">.zip</Chip>
            <Chip tone="cyan">.mrpack</Chip>
            <Chip>当前挂载目录</Chip>
          </div>
        </Reveal>
        <Reveal delay={38}>
          <div
            style={{
              marginTop: 6,
              padding: "15px 17px",
              borderRadius: 15,
              border: `1px solid ${COLORS.line}`,
              background: "rgba(8,16,20,0.55)",
              color: COLORS.inkMuted,
              fontFamily: FONT,
              fontSize: 15,
              lineHeight: 1.65,
              maxWidth: 470,
            }}
          >
            没有下载直链的清单项会保留本地文件，并在完成提示里报告跳过数量。
          </div>
        </Reveal>
      </div>

      <div style={{ position: "absolute", left: 640, top: 112 }}>
        <ShotCard
          src="shots/modpacks.png"
          width={1200}
          tilt={-1}
          glow="rgba(154,167,255,0.20)"
          label="DRAG & DROP"
        />
      </div>

      <div
        style={{
          position: "absolute",
          left: 1000,
          top: 720,
          translate: `${(1 - fly) * -280}px ${(1 - fly) * 100}px`,
          opacity: interpolate(frame, [26, 40], [0, 1], {
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
            border: "1px solid rgba(110,231,168,0.45)",
            boxShadow: "0 20px 60px rgba(0,0,0,0.5), 0 0 30px rgba(74,222,128,0.22)",
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
            <span style={{ fontFamily: FONT, fontSize: 15, color: COLORS.ink }}>
              整合包.zip
            </span>
            <span
              style={{
                fontFamily: FONT_MONO,
                fontSize: 10,
                letterSpacing: "0.08em",
                color: COLORS.inkMuted,
              }}
            >
              安装到当前实例
            </span>
          </div>
        </div>
      </div>
    </AbsoluteFill>
  );
};

const Outro120: React.FC = () => (
  <AbsoluteFill>
    <div
      style={{
        position: "absolute",
        inset: 0,
        display: "flex",
        flexDirection: "column",
        alignItems: "center",
        justifyContent: "center",
        gap: 26,
      }}
    >
      <Reveal>
        <Logo size={58} />
      </Reveal>
      <Reveal delay={6}>
        <Eyebrow>1.2.0 IS HERE</Eyebrow>
      </Reveal>
      <Reveal delay={14}>
        <Headline size={86} style={{ textAlign: "center" }}>
          现在就可以更新
        </Headline>
      </Reveal>
      <Reveal delay={24}>
        <div style={{ display: "flex", gap: 12 }}>
          <Chip tone="green">Windows</Chip>
          <Chip tone="cyan">便携单文件</Chip>
          <Chip tone="violet">官方源</Chip>
        </div>
      </Reveal>
      <Reveal delay={34}>
        <span
          style={{
            fontFamily: FONT_MONO,
            fontSize: 15,
            letterSpacing: "0.10em",
            color: COLORS.inkMuted,
          }}
        >
          QSLauncher-1.2.0-win-x64.exe
        </span>
      </Reveal>
    </div>
  </AbsoluteFill>
);

const SCENES = [
  { from: 0, duration: 110, label: "UPDATE" },
  { from: 110, duration: 220, label: "MEMORY" },
  { from: 330, duration: 190, label: "FROSTED" },
  { from: 520, duration: 220, label: "ZIP" },
  { from: 740, duration: 160, label: "OUTRO" },
];

export const UPDATE_DURATION = 900;

export const QSLauncherUpdate120: React.FC = () => {
  const frame = useCurrentFrame();
  const current =
    [...SCENES].reverse().find((scene) => frame >= scene.from) ?? SCENES[0];

  return (
    <AbsoluteFill style={{ fontFamily: FONT, backgroundColor: "#05080d" }}>
      <Backdrop />
      <Sequence from={0} durationInFrames={110} name="UPDATE">
        <Scene durationInFrames={110}>
          <Intro120 />
        </Scene>
      </Sequence>
      <Sequence from={110} durationInFrames={220} name="MEMORY">
        <Scene durationInFrames={220}>
          <Memory120 />
        </Scene>
      </Sequence>
      <Sequence from={330} durationInFrames={190} name="FROSTED">
        <Scene durationInFrames={190}>
          <Frost120 />
        </Scene>
      </Sequence>
      <Sequence from={520} durationInFrames={220} name="ZIP">
        <Scene durationInFrames={220}>
          <Modpack120 />
        </Scene>
      </Sequence>
      <Sequence from={740} durationInFrames={160} name="OUTRO">
        <Scene durationInFrames={160}>
          <Outro120 />
        </Scene>
      </Sequence>
      <Progress
        section={current.label}
        total={SCENES.length}
        index={SCENES.findIndex((scene) => scene.label === current.label) + 1}
      />
    </AbsoluteFill>
  );
};
