// NonToon Light Limit Changer —— postpixel 阶段（色相 / 伽马 / 饱和度）
//
// 在最终颜色合成完之后调整。处理顺序：色相 → 伽马 → 饱和度
// （饱和度放最后，单色化终态是纯灰、不受色相影响）。
// 同样只能写「语句 + 局部变量」。

{
    half llMask = saturate(sd.mask[_GlobalMaskChannel]);

    if (llMask > 0)
    {
        half3 llCol = sd.col.rgb;

        // 明度：整体亮度倍数作用在最终颜色上（颜色调整组，不受上下限夹取影响）
        llCol *= max(_Brightness, 0.0);

        // 色相：绕灰轴 (1,1,1)/√3 旋转，灰轴上的颜色保持不变
        // 角度 = _Hue * PI（-1..1 → ±180°），Rodrigues 向量形式避免声明矩阵
        half llAng = _Hue * 3.14159265;
        half llCos = cos(llAng);
        half llSin = sin(llAng);
        half3 llAxis = half3(0.57735, 0.57735, 0.57735);
        llCol = llCol * llCos + cross(llAxis, llCol) * llSin + llAxis * dot(llAxis, llCol) * (1.0 - llCos);

        // 伽马：pow(col, gamma)，1 = 不改变；防 0 次幂全白
        llCol = pow(max(llCol, 0.0), max(_Gamma, 0.001));

        // 饱和度：0 = 无彩色，1 = 原样，2 = 加深
        half llLuma = dot(llCol, half3(0.2126, 0.7152, 0.0722));
        llCol = lerp(half3(llLuma, llLuma, llLuma), llCol, _Saturation);

        sd.col.rgb = lerp(sd.col.rgb, llCol, llMask);
    }
}
