SC_Box
SC_float(_Min, 0.0, [SCRange(0,1)], "Min Brightness", "亮度下限：环境再暗也不会低于这个亮度（0 = 不限制）")
SC_float(_Max, 1.0, [SCRange(0,2)], "Max Brightness", "亮度上限：环境再亮也不会高于这个亮度（1 = NonToon 原本的上限）")
SC_BoxEnd

SC_float(_Brightness, 1.0, [SCRange(0,4)], "Brightness", "明度（整体亮度倍数），1 = 不改变；作用于最终颜色，不受上下限夹取影响")
SC_float(_Gamma, 1.0, [SCRange(0,2)], "Gamma", "伽马，1 = 不改变，<1 提亮中间调，>1 压暗中间调")
SC_float(_Hue, 0.0, [SCRange(-1,1)], "Hue", "色相，0 = 不改变，±1 = ±180° 旋转（绕灰轴）")
SC_float(_Saturation, 1.0, [SCRange(0,2)], "Saturation", "饱和度，1 = 不改变，0 = 无彩色，2 = 加深")
SC_float(_Monochrome, 0.0, [SCRange(0,1)], "Monochrome", "灯光单色化，削弱彩色灯光的影响（对灯光色去饱和，0 = 不改变）")
SC_float(_Unlit, 0.0, [SCRange(0,1)], "Unlit", "无光照化，1 = 完全不受光照影响（按全亮渲染）")
SC_float(_ColorTemp, 0.5, [SCRange(0,1)], "Color Temp", "色温，0.5 = 不改变，0 = 偏暖，1 = 偏冷")
SC_uint(_GlobalMaskChannel, 3, [SCMaskChannel], "__MaskChannel", "共享遮罩里控制生效范围的通道（默认 A，没有遮罩时该通道为 1 = 整体生效）")
