import discord
from discord.ext import commands
import asyncio
import io

# ============================================================
# CONFIGURATION — Set these to match your Blackwall settings
# ============================================================
# Rate limit test: send RATE_LIMIT_COUNT + 1 unique messages
# within RATE_LIMIT_WINDOW seconds
RATE_LIMIT_COUNT = 5       # should match config.MaxMessagesPerWindow
RATE_LIMIT_WINDOW = 5      # should match config.RateLimitWindowSeconds

# Duplicate test: send DUPLICATE_THRESHOLD identical messages
# within DUPLICATE_WINDOW seconds
DUPLICATE_THRESHOLD = 3    # should match config.DuplicateMessageThreshold
DUPLICATE_WINDOW = 5       # should match config.DuplicateWindowSeconds

# ============================================================
# BOT SETUP — Use a BOT TOKEN
# Enable IsTestMode in your Blackwall guild settings to allow
# bot messages to be evaluated by the spam filter.
# ============================================================
intents = discord.Intents.default()
intents.message_content = True

bot = commands.Bot(command_prefix='!', intents=intents)

_unique_counter = 0

def _unique_msg():
    global _unique_counter
    _unique_counter += 1
    return f"Rate limit test unique payload #{_unique_counter}"


@bot.event
async def on_ready():
    print(f'Spam Simulator logged in as {bot.user} (bot={bot.user.bot})')


# ============================================================
# RATE LIMIT TESTS
# Send UNIQUE messages rapidly to trigger only the rate limiter.
# Messages are unique so duplicate detection will NOT fire.
# ============================================================

@bot.command()
async def test1(ctx):
    """Rate limit: send RATE_LIMIT_COUNT+1 unique messages in one channel."""
    total = RATE_LIMIT_COUNT + 1
    delay = max(0.1, (RATE_LIMIT_WINDOW - 1) / total)
    await ctx.send(f"Starting Test 1: {total} unique messages in {RATE_LIMIT_WINDOW}s...")
    for _ in range(total):
        await ctx.send(_unique_msg())
        await asyncio.sleep(delay)


@bot.command()
async def test2(ctx, channel1: discord.TextChannel, channel2: discord.TextChannel, channel3: discord.TextChannel):
    """Rate limit: send unique messages across 3 channels rapidly."""
    channels = [channel1, channel2, channel3]
    total = RATE_LIMIT_COUNT + 1
    delay = max(0.1, (RATE_LIMIT_WINDOW - 1) / total)
    await ctx.send(f"Starting Test 2: {total} unique messages across 3 channels in {RATE_LIMIT_WINDOW}s...")
    for i in range(total):
        ch = channels[i % len(channels)]
        await ch.send(_unique_msg())
        await asyncio.sleep(delay)


# ============================================================
# DUPLICATE TESTS
# Send IDENTICAL messages to trigger duplicate detection.
# Sent slowly enough to avoid triggering rate limit
# (DUPLICATE_THRESHOLD should be <= RATE_LIMIT_COUNT).
# ============================================================

@bot.command()
async def test3(ctx):
    """Duplicate: send DUPLICATE_THRESHOLD identical messages in one channel."""
    delay = max(0.2, (DUPLICATE_WINDOW - 0.5) / DUPLICATE_THRESHOLD)
    await ctx.send(f"Starting Test 3: {DUPLICATE_THRESHOLD} identical messages in {DUPLICATE_WINDOW}s...")
    for _ in range(DUPLICATE_THRESHOLD):
        await ctx.send("Duplicate test identical payload A")
        await asyncio.sleep(delay)


@bot.command()
async def test4(ctx, channel1: discord.TextChannel, channel2: discord.TextChannel, channel3: discord.TextChannel):
    """Duplicate: send identical messages across 3 channels.
    Requires DuplicateCrossChannelEnabled = true in Blackwall config."""
    channels = [channel1, channel2, channel3]
    delay = max(0.2, (DUPLICATE_WINDOW - 0.5) / DUPLICATE_THRESHOLD)
    await ctx.send(f"Starting Test 4: {DUPLICATE_THRESHOLD} identical messages across 3 channels in {DUPLICATE_WINDOW}s...")
    for i in range(DUPLICATE_THRESHOLD):
        ch = channels[i % len(channels)]
        await ch.send("Duplicate test identical payload B")
        await asyncio.sleep(delay)


# ============================================================
# DUPLICATE WITH IMAGES TESTS
# ExtractFullContent only hashes text, not attachments.
# Identical text + same image = same hash = duplicate detected.
# ============================================================

@bot.command()
async def test5(ctx):
    """Duplicate: send DUPLICATE_THRESHOLD identical messages with images in one channel."""
    delay = max(0.2, (DUPLICATE_WINDOW - 0.5) / DUPLICATE_THRESHOLD)
    await ctx.send(f"Starting Test 5: {DUPLICATE_THRESHOLD} identical messages with images in {DUPLICATE_WINDOW}s...")
    dummy_image = io.BytesIO(b'\x89PNG\r\n\x1a\n\x00\x00\x00\rIHDR\x00\x00\x00\x01\x00\x00\x00\x01\x08\x06\x00\x00\x00\x1f\x15\xc4\x89\x00\x00\x00\nIDATx\x9cc\x00\x01\x00\x00\x05\x00\x01\r\n-\xb4\x00\x00\x00\x00IEND\xaeB`\x82')
    for _ in range(DUPLICATE_THRESHOLD):
        dummy_image.seek(0)
        file = discord.File(fp=dummy_image, filename="spam_image.png")
        await ctx.send("Duplicate test identical payload C with image", file=file)
        await asyncio.sleep(delay)


@bot.command()
async def test6(ctx, channel1: discord.TextChannel, channel2: discord.TextChannel, channel3: discord.TextChannel):
    """Duplicate: send identical messages with images across 3 channels.
    Requires DuplicateCrossChannelEnabled = true in Blackwall config."""
    channels = [channel1, channel2, channel3]
    delay = max(0.2, (DUPLICATE_WINDOW - 0.5) / DUPLICATE_THRESHOLD)
    await ctx.send(f"Starting Test 6: {DUPLICATE_THRESHOLD} identical messages with images across 3 channels in {DUPLICATE_WINDOW}s...")
    dummy_image = io.BytesIO(b'\x89PNG\r\n\x1a\n\x00\x00\x00\rIHDR\x00\x00\x00\x01\x00\x00\x00\x01\x08\x06\x00\x00\x00\x1f\x15\xc4\x89\x00\x00\x00\nIDATx\x9cc\x00\x01\x00\x00\x05\x00\x01\r\n-\xb4\x00\x00\x00\x00IEND\xaeB`\x82')
    for i in range(DUPLICATE_THRESHOLD):
        dummy_image.seek(0)
        file = discord.File(fp=dummy_image, filename="spam_image.png")
        ch = channels[i % len(channels)]
        await ch.send("Duplicate test identical payload D with image", file=file)
        await asyncio.sleep(delay)


# ============================================================
# RUN ALL TESTS SEQUENTIALLY
# ============================================================

@bot.command()
async def testall(ctx, channel1: discord.TextChannel, channel2: discord.TextChannel, channel3: discord.TextChannel):
    """Run all 6 tests sequentially with pauses between each."""
    await ctx.send("Running all tests sequentially. Wait for completion...")
    await asyncio.sleep(2)

    await ctx.send(">>> TEST 1: Rate limit (same channel)")
    await test1(ctx)
    await asyncio.sleep(RATE_LIMIT_WINDOW + 2)

    await ctx.send(">>> TEST 2: Rate limit (cross-channel)")
    await test2(ctx, channel1, channel2, channel3)
    await asyncio.sleep(RATE_LIMIT_WINDOW + 2)

    await ctx.send(">>> TEST 3: Duplicate (same channel)")
    await test3(ctx)
    await asyncio.sleep(DUPLICATE_WINDOW + 2)

    await ctx.send(">>> TEST 4: Duplicate (cross-channel)")
    await test4(ctx, channel1, channel2, channel3)
    await asyncio.sleep(DUPLICATE_WINDOW + 2)

    await ctx.send(">>> TEST 5: Duplicate with images (same channel)")
    await test5(ctx)
    await asyncio.sleep(DUPLICATE_WINDOW + 2)

    await ctx.send(">>> TEST 6: Duplicate with images (cross-channel)")
    await test6(ctx, channel1, channel2, channel3)
    await ctx.send("All tests complete.")


# Error handling for missing channel arguments
@test2.error
@test4.error
@test6.error
@testall.error
async def channel_error(ctx, error):
    if isinstance(error, commands.MissingRequiredArgument):
        await ctx.send("Please provide 3 valid channel mentions. Example: `!test2 #channel1 #channel2 #channel3`")


bot.run('MTUyMjUxNjk1NzU0NTYyNzY4MQ.G02dTg.H7T7eV3WdkFB6v7GTq9xYOkkY-AB_EETXnq5qw')
