// NonToon Light Limit Changer —— modifylight 阶段
//
// .scmodule 里声明 afters: ["Lighten"]，在 NonToon 自带 Light Boost 之后执行，两者可叠加。
// 只能写「语句 + 局部变量」（原样插进片段函数体），不能声明全局变量 / 定义函数 / static const。
// 处理顺序：灯光单色化 → 色温 → luma 归一夹取（上下限）→ Unlit 拍平。
// （明度/色相/伽马/饱和度在 postpixel 阶段作用于最终颜色，见 phase_postpixel.hlsl）

{
    // 共享遮罩里用于限制生效范围的通道；没设遮罩时该通道为 1，即整体生效
    half llMask = saturate(sd.mask[_GlobalMaskChannel]);

    if (llMask > 0)
    {
        half llLo = min(_Min, _Max);
        half llHi = max(_Min, _Max);

        half3 llColor = sd.lightColor;

        // 灯光单色化：对灯光色去饱和，削弱彩色灯光的影响（保留 albedo 颜色）
        half llLightLuma = dot(llColor, half3(0.2126, 0.7152, 0.0722));
        llColor = lerp(llColor, half3(llLightLuma, llLightLuma, llLightLuma), saturate(_Monochrome));

        // 色温（与 Light Limit Changer 相同的三段曲线：0 = 暖，0.5 = 原样，1 = 冷）
        half3 llTint;
        half llWarm = saturate(_ColorTemp * 2.0);
        half llCool = saturate((_ColorTemp - 0.5) * 2.0);
        llTint.r = lerp(0.6, 1.0, llWarm);
        llTint.g = lerp(0.95, 1.0, llWarm);
        llTint.b = lerp(1.0, 0.6, llCool);
        llColor *= llTint;

        // 按亮度（luma）归一到 [下限, 上限] 再乘回去：
        // 暗部抬高、高光压住，颜色之间的明暗比例保持不变（不会把阴影拍平）
        half3 llLumaWeight = half3(0.2126, 0.7152, 0.0722);
        half llLuma = max(dot(llColor, llLumaWeight), 1e-5);
        llColor *= clamp(llLuma, llLo, llHi) / llLuma;

        // Unlit：把光照拍平成全亮
        llColor = lerp(llColor, half3(1.0, 1.0, 1.0), saturate(_Unlit));

        sd.lightColor = lerp(sd.lightColor, llColor, llMask);
    }
}
