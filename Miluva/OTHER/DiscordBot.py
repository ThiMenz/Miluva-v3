import discord

TOKEN_PATH = r"C:\Users\tpmen\Desktop\4 - Programming\41 - Unity & C#\ThiNetBotToken.txt"

intents = discord.Intents.default()
intents.message_content = True

client = discord.Client(intents=intents)

TFILE = open(TOKEN_PATH, 'r')
TOKEN = TFILE.read()
TFILE.close()

@client.event
async def on_ready():
    print(f'We have logged in as {client.user}')

@client.event
async def on_message(message):
    if message.author == client.user:
        return

    MFILE = open("DiscordMessages.txt", "w")
    MFILE.write(str(message.author.id) + ": " + str(message.content))
    MFILE.close()

client.run(TOKEN)

MFILE = open("DiscordMessages.txt", "w")
MFILE.write("")
MFILE.close()