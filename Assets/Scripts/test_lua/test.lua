-- 基本数据类型
local str = "Hello, Lua!"
local num = 42
local bool = true
local nilValue = nil
 
-- 变量赋值
local x = 1
x = x + 5
 
-- 条件语句
if x > 10 then
  print("x is greater than 10")
elseif x < 10 then
  print("x is less than 10")
else
  print("x is equal to 10")
end
 
-- 循环语句
for i = 1, 5 do
  print("Count 1: " .. i)
end
 
local i = 1
while i <= 5 do
  print("Count 2: " .. i)
  i = i + 1
end
 
repeat
  print("Count 3: " .. i)
  i = i - 1
until i == 0
 