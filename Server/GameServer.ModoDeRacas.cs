using System.Text.Json;
using Godot;
using Jandirus.Core.Races;
using Jandirus.Net;
using LiteNetLib;

namespace Jandirus.Server;

/// <summary>
/// ============================ O MODO DAS RACAS DESTE SERVIDOR (v0.3.0, item 34 do pedido 15) ============================
/// O pedido: *"ao criar um server/hostear o criador vai ter uma tela q vai ter 2 opçoes, raças aleatorias onde o jogador
/// n pode escolher sua raça inicial, sendo aleatoria, e o modo padrao, onde o jogador escolhe, em servers dedicados como
/// vps, um adm pode entrar no jogo, e na aba de adm no meu P vai ter a opçao de mudar essa config mesmo dps do server ter
/// sido criado, assim futuros jogadores serao random ou padrao dependendo da escolha dele"*.
///
/// TRES PORTAS, UM ESTADO:
///   * quem HOSPEDA escolhe na tela de hospedar (`Boot.PerguntarOModoEHospedar`), antes de o servidor subir;
///   * o ADMIN muda pela aba Admin do menu P (o verb `admin_modo_racas`) a qualquer hora -- e o caminho do servidor
///     dedicado, que nao tem tela de hospedar;
///   * o modo mora no `servidor.json` da pasta de saves e SOBREVIVE a limpeza total: ele e configuracao do SERVIDOR, e nao
///     estado do mundo (a limpeza devolve o primeiro boot do mundo; o modo foi escolhido por quem hospeda).
/// "Futuros jogadores" e literal: quem ja tem personagem fica com ele; o modo vale pra cada personagem que NASCER depois.
///
/// ============================ O SORTEIO E POR VAGA, E FICA GRAVADO NA CONTA ============================
/// A raca sai quando o servidor manda a lista de vagas (`MandarSlots`): cada vaga VAZIA ganha o seu sorteio, gravado na
/// conta (`AccountSave.Sorteios`). Gravado, e nao sorteado so no fim da criacao, por dois motivos:
///   * a tela de criacao PRECISA da raca antes de comecar -- as etapas de genero, corpo, cabelo e as formas do Frost Demon
///     dependem dela (`CreationScreen`);
///   * sair e entrar de novo NAO sorteia outra vez: o sorteio de uma vaga e um so, ate um personagem nascer nela.
/// O servidor CONFERE na criacao (<see cref="AplicarOSorteio"/>): no modo aleatorio a ficha nasce com a raca da vaga, diga
/// o cliente o que disser -- esconder as etapas da tela e conveniencia, e nao trava.
/// ==========================================================================================================
/// </summary>
public partial class GameServer
{
	/// <summary>O arquivo da configuracao do servidor, na pasta de saves. Hoje so o modo das racas.</summary>
	public const string NomeDoArquivoDoServidor = "servidor.json";

	/// <summary>O modo de agora. Comeca no padrao; o `servidor.json` o substitui no boot (<see cref="CarregarConfiguracaoDoServidor"/>).</summary>
	internal ModoDeRacas ModoDeRacas { get; private set; } = ModoDeRacas.Padrao;

	/// <summary>O que vai pro `servidor.json`. O modo vai por EXTENSO ("padrao"/"aleatorio"): o arquivo e de quem administra o servidor, e ele le texto.</summary>
	private sealed class ConfiguracaoDoServidor
	{
		public string ModoDeRacas { get; set; } = "padrao";
	}

	private static string TextoDoModo(ModoDeRacas modo) => modo == ModoDeRacas.Aleatorio ? "aleatorio" : "padrao";

	// as grafias de sempre (com e sem acento) e o "default"/"random" do aviso em ingles -- ver `PalavrasDeComando`
	private static ModoDeRacas? ModoDoTexto(string texto) =>
		Jandirus.Core.Lingua.PalavrasDeComando.E(texto, "padrao") ? ModoDeRacas.Padrao
		: Jandirus.Core.Lingua.PalavrasDeComando.E(texto, "aleatorio") ? ModoDeRacas.Aleatorio
		: null;

	/// <summary>A pasta de saves deste processo -- a mesma do `AccountStore` (`CarregarVisual`), sem precisar dele de pe.</summary>
	private static string PastaDosSaves() => ProjectSettings.GlobalizePath("user://saves");

	/// <summary>
	/// O MODO GRAVADO NESTA PASTA DE SAVES, sem subir o servidor -- e o que a tela de hospedar mostra como "o modo de hoje"
	/// antes de quem hospeda escolher. Sem arquivo (ou ilegivel) e o padrao.
	/// </summary>
	public static ModoDeRacas ModoDeRacasGravado() => LerModo(Path.Combine(PastaDosSaves(), NomeDoArquivoDoServidor));

	private static ModoDeRacas LerModo(string caminho)
	{
		try
		{
			if (!File.Exists(caminho)) return ModoDeRacas.Padrao;
			ConfiguracaoDoServidor? c = JsonSerializer.Deserialize<ConfiguracaoDoServidor>(File.ReadAllText(caminho));
			return ModoDoTexto(c?.ModoDeRacas ?? "") ?? ModoDeRacas.Padrao;
		}
		catch (Exception e)
		{
			GD.PushWarning($"[server] {NomeDoArquivoDoServidor} ilegivel ({e.Message}) -- o modo das racas fica no padrao");
			return ModoDeRacas.Padrao;
		}
	}

	/// <summary>No boot, logo depois da semente (`CarregarVisual`): o modo gravado vale de novo.</summary>
	private void CarregarConfiguracaoDoServidor()
	{
		ModoDeRacas = LerModo(Path.Combine(_store?.Pasta ?? PastaDosSaves(), NomeDoArquivoDoServidor));
		GD.Print($"[server] racas dos personagens novos: {SorteioDeRaca.Nome(ModoDeRacas)}");
	}

	/// <summary>
	/// MUDA O MODO, e grava. As duas portas passam aqui: a tela de hospedar (antes de o servidor subir -- o boot le o
	/// arquivo que esta linha acabou de escrever) e o verb de admin. <paramref name="quem"/> vai pro console.
	/// </summary>
	public void EscolherModoDeRacas(ModoDeRacas modo, string quem)
	{
		ModoDeRacas = modo;
		string pasta = _store?.Pasta ?? PastaDosSaves();
		try
		{
			Directory.CreateDirectory(pasta);
			string caminho = Path.Combine(pasta, NomeDoArquivoDoServidor);
			File.WriteAllText(caminho, JsonSerializer.Serialize(new ConfiguracaoDoServidor { ModoDeRacas = TextoDoModo(modo) },
																 new JsonSerializerOptions { WriteIndented = true }));
		}
		catch (Exception e) { GD.PushWarning($"[server] nao deu pra gravar o {NomeDoArquivoDoServidor}: {e.Message}"); }
		GD.Print($"[server] racas dos personagens novos: {SorteioDeRaca.Nome(modo)} (por {quem})");
	}

	/// <summary>
	/// AS VAGAS VAZIAS NO MODO DE AGORA, antes de toda lista de vagas: no aleatorio, cada vaga vazia sem sorteio ganha o
	/// dela; no padrao (e em vaga ocupada), sorteio velho nenhum fica pendurado. Grava a conta quando muda alguma coisa.
	/// </summary>
	private void PrepararVagas(AccountSave acc)
	{
		bool mudou = false;
		for (int i = 0; i < AccountStore.Slots; i++)
		{
			if (ModoDeRacas == ModoDeRacas.Aleatorio && acc.Slots[i] == null)
			{
				if (acc.Sorteios[i] != null) continue;
				// (2026-10-16) COM A CLASSE RARA GARANTIDA PELO ADMIN, so entre as racas que TEM classe rara: *"no modo aleatorio a
				// raça vem aleatoria mas sempre da classe mais rara"*. Ver `GameServer.Admin.cs`, `AdminClasseRara`.
				(string raca, string linhagem) = SorteioDeRaca.Sortear(
					SorteioDeRaca.SementeDe(SeedDoUniverso, acc.Conta, i, acc.SorteiosFeitos),
					acc.ProximaClasseRara && _racas != null ? r => Birth.TemClasseRara(_racas, r) : null);
				acc.Sorteios[i] = new RacaSorteada { Raca = raca, Linhagem = linhagem };
				acc.SorteiosFeitos++;
				mudou = true;
			}
			else if (acc.Sorteios[i] != null) { acc.Sorteios[i] = null; mudou = true; }
		}
		if (mudou) _store?.Gravar(acc);
	}

	/// <summary>
	/// A CRIACAO NO MODO ALEATORIO: a ficha nasce com a raca SORTEADA da vaga (e a sub-raca dela), e nao com a que o cliente
	/// mandou. A tela ja manda a sorteada; o caso de verdade e o do modo que MUDOU com o jogador na tela de criacao -- a
	/// vaga ganha o sorteio agora, e a ficha se ajusta a ele (idade no teto da raca nova, genero de quem nao tem).
	/// </summary>
	private void AplicarOSorteio(AccountSave acc, int slot, CharacterDraft ficha)
	{
		if (ModoDeRacas != ModoDeRacas.Aleatorio || SorteioIgnoradoDeTeste) return;
		PrepararVagas(acc);
		if (acc.Sorteios[slot] is not { } s) return;
		if (ficha.Race != s.Raca || ficha.ChosenClass != s.Linhagem)
			GD.Print($"[server] {acc.Conta}: a ficha pedia {ficha.Race}/{ficha.ChosenClass} e a vaga {slot + 1} sorteou "
				   + $"{s.Raca}/{s.Linhagem} -- vale o sorteio");
		ficha.Race = s.Raca;
		ficha.ChosenClass = s.Linhagem;
		ficha.Planet = SorteioDeRaca.PlanetaDaRaca(s.Raca);
		ficha.Age = Math.Clamp(ficha.Age, 1, ficha.IdadeMaxima);
		if (!ficha.TemGenero) ficha.Gender = "Male";
	}

	/// <summary>DEFEITO INJETADO (`--v030teste`): a criacao aceita a raca que o cliente mandou mesmo no modo aleatorio -- o sorteio vira so a tela.</summary>
	internal static bool SorteioIgnoradoDeTeste;

	/// <summary>
	/// O VERB `admin_modo_racas` -- a aba Admin do menu P. Sem argumento so responde o modo de agora (e por isso nao entra
	/// no `admin.log`: perguntar nao e agir); com "padrao" ou "aleatorio", muda, grava e avisa os admins online.
	/// </summary>
	private void AdminModoDeRacas(ServerPlayer adm, string arg)
	{
		if (arg.Trim().Length > 0)
		{
			if (ModoDoTexto(arg) is not { } novo) { Avisar(adm, "use: admin_modo_racas padrao | aleatorio"); return; }
			if (novo != ModoDeRacas)
			{
				EscolherModoDeRacas(novo, $"{adm.Name} ({adm.Conta})");
				string frase = novo == ModoDeRacas.Aleatorio
					? "a partir de agora, a raça inicial dos personagens novos é SORTEADA."
					: "a partir de agora, cada jogador escolhe a raça inicial do personagem novo.";
				foreach (ServerPlayer o in Jogadores)
					if (EhAdmin(o)) { Avisar(o, $"[admin] {adm.Name}: {frase}"); MandarModoDeRacas(o); }
			}
		}
		MandarModoDeRacas(adm);
	}

	/// <summary>O modo de agora, pro painel da aba Admin (`S2C.ModoDeRacas`: um byte).</summary>
	private void MandarModoDeRacas(ServerPlayer pl)
	{
		if (pl.Peer == null) return;
		var w = Protocol.Begin(Protocol.S2C.ModoDeRacas);
		w.Put((byte)ModoDeRacas);
		pl.Peer.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}
}
