using Godot;
using Jandirus.Core;
using Jandirus.Net;
using LiteNetLib;
using LiteNetLib.Utils;

namespace Jandirus.Server;

/// <summary>
/// A BANCADA DA VERSAO (`--versaoteste`) -- o lado do SERVIDOR do aperto de mao de versao.
///
/// O dono (2026-10-11): *"ao entrar em um server o server checa a versao do cliente / cliente checa a versao do
/// server, e se n bater o jogo manda pro menu avisando q o cliente ta em alguma versao na frente ou atras"*.
///
/// ELA RODA DEPOIS DE A PORTA ABRIR (e a unica das de boot que precisa disso), porque a familia 4 BATE NA PORTA
/// DE VERDADE: um `NetManager` de bancada disca em 127.0.0.1 com o pedido que ela quiser -- o do cliente de
/// hoje, o do cliente antigo, lixo -- e quem responde e a porta de producao (`AoPedirConexao`), pelo fio. Como o
/// servidor ainda nao esta sendo processado pela arvore nesta hora, e a propria bancada que bombeia os dois
/// lados (`DiscarDeBancada`).
///
///   1. A REGRA (Core): o texto e o numero, a comparacao com a direcao, as frases, a copia do project.godot.
///   2. A IMPRESSAO DIGITAL do protocolo: estavel, feita de nome e valor, e sensivel a opcode e a campo.
///   3. O FIO (puro): o bloco ida e volta, todo corte dele, lixo, e o juizo de cada pedido.
///   4. A PORTA, pelo fio: igual entra (e o primeiro pacote e a versao daqui); atras e na frente sao recusados
///      com a versao do servidor na carga; sem versao e recusado limpo; lixo nao derruba; outro protocolo e pego.
///
/// Os defeitos injetados: `Versao.DigitalForaDaContaDeTeste`, `AceitaQualquerVersaoDeTeste`,
/// `CargaDaRecusaDeTeste` e `PortaAntigaDeTeste`. O lado do CLIENTE (o menu, o painel, o prazo do servidor
/// antigo) e da `--diagversao`; os dois processos de verdade, do `ag/versao/bancadas.sh`.
///
///     Godot --headless --path . --server --port 8280 --versaoteste
/// </summary>
public partial class GameServer
{
	private int _vsOk, _vsFalhou;

	private void AfirmarVs(string nome, bool cond, string detalhe = "")
	{
		if (cond) { _vsOk++; GD.Print($"[versao]   OK    {nome}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); }
		else { _vsFalhou++; GD.PrintErr($"[versao]   FALHA {nome}   [{detalhe}]"); }
	}

	/// <summary>O que voltou de uma discagem de bancada.</summary>
	private sealed record DiscagemDeTeste(bool Aceito, bool Recusado, byte[] Carga, byte[]? PrimeiroPacote, long Ms, string Fim);

	/// <summary>Quanto a discagem de bancada espera o primeiro pacote depois do aceite (a porta antiga nunca o manda).</summary>
	private const int EsperaDoPrimeiroPacoteDeTesteMs = 700;

	/// <summary>
	/// DISCA NA PORTA DESTE SERVIDOR, pelo fio, com o pedido dado -- um `NetManager` cru, e nao o `GameClient`:
	/// o que se mede aqui e o que a PORTA responde a cada pedido, inclusive aos que o cliente de hoje nunca
	/// escreveria. Bombeia as duas pontas ate o desfecho.
	/// </summary>
	private DiscagemDeTeste DiscarDeBancada(int porta, NetDataWriter pedido, int prazoMs = 2500)
	{
		var ouvinte = new EventBasedNetListener();
		var cli = new NetManager(ouvinte) { AutoRecycle = true, UpdateTime = 5, ChannelsCount = Protocol.TotalDeCanais };
		bool conectou = false, caiu = false, recusou = false;
		byte[] carga = [];
		byte[]? primeiro = null;
		string fim = "prazo";
		long conectouEm = -1;
		var relogio = System.Diagnostics.Stopwatch.StartNew();

		ouvinte.PeerConnectedEvent += _ => { conectou = true; conectouEm = relogio.ElapsedMilliseconds; };
		ouvinte.NetworkReceiveEvent += (_, r, _, _) => primeiro ??= r.GetRemainingBytes();
		ouvinte.PeerDisconnectedEvent += (_, info) =>
		{
			caiu = true;
			fim = info.Reason.ToString();
			recusou = info.Reason == DisconnectReason.ConnectionRejected;
			if (info.AdditionalData is { IsNull: false, AvailableBytes: > 0 } ad) carga = ad.GetRemainingBytes();
		};

		cli.Start();
		try
		{
			cli.Connect("127.0.0.1", porta, pedido);
			while (relogio.ElapsedMilliseconds < prazoMs)
			{
				_net.PollEvents();
				cli.PollEvents();
				if (caiu) break;
				if (conectou && primeiro != null) { fim = "aceito, com o primeiro pacote"; break; }
				if (conectou && relogio.ElapsedMilliseconds - conectouEm > EsperaDoPrimeiroPacoteDeTesteMs) { fim = "aceito, e nenhum pacote veio"; break; }
				Thread.Sleep(2);
			}
		}
		finally
		{
			cli.Stop();
			// O SERVIDOR LE A DESPEDIDA DO PAR DE BANCADA antes do proximo caso (senao ela chegaria no meio dele).
			for (int i = 0; i < 15; i++) { _net.PollEvents(); Thread.Sleep(2); }
		}
		return new DiscagemDeTeste(conectou, recusou, carga, primeiro, relogio.ElapsedMilliseconds, fim);
	}

	/// <summary>Um pedido de conexao de bancada: a senha dada e, se houver, um bloco de versao -- ou bytes crus depois da senha.</summary>
	private static NetDataWriter PedidoDeTeste(string? senha, IdentidadeDeBuild? versao = null, byte[]? cru = null)
	{
		var w = new NetDataWriter();
		if (senha != null) w.Put(senha);
		if (versao is { } v) FioDaVersao.EscreverBloco(w, v);
		if (cru != null) w.Put(cru);
		return w;
	}

	/// <summary>Le um bloco de versao de um vetor de bytes, com o leitor de PRODUCAO; devolve tambem quanto sobrou.</summary>
	private static (FioDaVersao.Leitura Leitura, IdentidadeDeBuild Id, int Sobra) LerBlocoDeTeste(byte[] bytes, int pular = 0)
	{
		var r = new NetDataReader(bytes, pular, bytes.Length);
		FioDaVersao.Leitura l = FioDaVersao.LerBloco(r, out IdentidadeDeBuild id);
		return (l, id, r.AvailableBytes);
	}

	public void RodarBancadaDaVersao(int porta)
	{
		_vsOk = _vsFalhou = 0;
		GD.Print("[versao] ================ A VERSAO DO JOGO E O APERTO DE MAO (dono, 2026-10-11) ================");

		IdentidadeDeBuild? versaoAntes = VersaoDeTeste;
		IdentidadeDeBuild daqui = MinhaVersao;
		var atras = new IdentidadeDeBuild(daqui.Numero - 1, daqui.Digital);
		var naFrente = new IdentidadeDeBuild(daqui.Numero + 1, daqui.Digital);
		var outroBuild = new IdentidadeDeBuild(daqui.Numero, daqui.Digital ^ FioDaVersao.MascaraDaForja);
		GD.Print($"[versao]   este servidor: versao {daqui.Texto} (numero {daqui.Numero}), protocolo {daqui.Digital:x16}");

		try
		{
			// ---------------------------------------------------------------- 1. a regra
			GD.Print("[versao] -- 1) a regra: o texto, o numero, a direcao e as frases --");
			AfirmarVs("UMA FONTE SO: o texto do menu e o numero do fio saem da mesma linha (o texto e o numero refeito)",
					  Versao.Texto == Versao.TextoDe(Versao.Numero) && Versao.Texto == $"{Versao.Maior}.{Versao.Menor}.{Versao.Remendo}"
					  && Versao.TentarLer(Versao.Texto, out int relido) && relido == Versao.Numero, $"\"{Versao.Texto}\" <-> {Versao.Numero}");
			int[] partes = [Versao.Maior, Versao.Menor, Versao.Remendo];
			AfirmarVs("as tres partes cabem no teto (0..999 cada), e o numero ordena como a versao: 0.1.9 < 0.2.0 < 1.0.0",
					  partes.All(p => p >= 0 && p <= Versao.TetoDaParte)
					  && Versao.TentarLer("0.1.9", out int a19) && Versao.TentarLer("0.2.0", out int a20) && Versao.TentarLer("1.0.0", out int a100)
					  && a19 < a20 && a20 < a100, string.Join(".", partes));
			int[] amostra = [0, 1, 999, 1_000, 1_999, 1_000_000, 12_034_056, Versao.NumeroMaximo];
			AfirmarVs("ida e volta do texto em oito numeros (do 0.0.0 ao 999.999.999)",
					  amostra.All(n => Versao.TentarLer(Versao.TextoDe(n), out int v) && v == n),
					  string.Join(" ", amostra.Select(Versao.TextoDe)));
			string[] tortos = ["", "1", "1.2", "1.2.3.4", "a.b.c", "1.2.1000", "-1.0.0", "1..2", "1.2.x", "0,1,0", "v0.1.0"];
			AfirmarVs("texto que nao e versao e recusado (faltando parte, parte demais, letra, quatro digitos, sinal, virgula)",
					  tortos.All(t => !Versao.TentarLer(t, out _)) && Versao.TentarLer(" 0.1.0 ", out int aparado) && aparado == 1000,
					  string.Join(" | ", tortos.Where(t => Versao.TentarLer(t, out _))));
			AfirmarVs("numero que nao e versao nao vira texto: negativo e acima do teto saem \"?\"",
					  Versao.TextoDe(-1) == "?" && Versao.TextoDe(Versao.NumeroMaximo + 1) == "?" && Versao.TextoDe(int.MinValue) == "?");

			AfirmarVs("A DIRECAO: jogo atras = ATRASADO, jogo na frente = ADIANTADO, igual = nenhum desencontro",
					  Versao.Comparar(atras, daqui) == Versao.Desencontro.JogoAtrasado
					  && Versao.Comparar(naFrente, daqui) == Versao.Desencontro.JogoAdiantado
					  && Versao.Comparar(daqui, daqui) == Versao.Desencontro.Nenhum);
			AfirmarVs("a direcao vem do NUMERO, mesmo com o protocolo tambem diferente (atras + outra digital continua ATRASADO)",
					  Versao.Comparar(new IdentidadeDeBuild(daqui.Numero - 1, ~daqui.Digital), daqui) == Versao.Desencontro.JogoAtrasado);
			AfirmarVs("**MESMA VERSAO + OUTRA DIGITAL = OUTRO BUILD** (o protocolo mudou e a versao nao subiu)",
					  Versao.Comparar(outroBuild, daqui) == Versao.Desencontro.OutroBuild);
			Versao.DigitalForaDaContaDeTeste = true;
			try
			{
				AfirmarVs("(defeito injetado: a comparacao olha so o numero) o outro build passa por igual -- a regua sabe reprovar",
						  Versao.Comparar(outroBuild, daqui) == Versao.Desencontro.Nenhum);
			}
			finally { Versao.DigitalForaDaContaDeTeste = false; }

			var jogoVelho = new IdentidadeDeBuild(2_001, 0xAAAA_AAAA_0000_0001UL);    // 0.2.1
			var servNovo = new IdentidadeDeBuild(3_000, 0xBBBB_BBBB_0000_0002UL);     // 0.3.0
			Versao.Aviso? fAtras = Versao.AvisoDoDesencontro(jogoVelho, servNovo);
			Versao.Aviso? fFrente = Versao.AvisoDoDesencontro(servNovo, jogoVelho);
			Versao.Aviso? fBuild = Versao.AvisoDoDesencontro(jogoVelho, jogoVelho with { Digital = servNovo.Digital });
			AfirmarVs("A FRASE DO ATRASADO diz as duas versoes, a direcao e o que fazer",
					  fAtras is { } fa && fa.Frase == "Este servidor está na versão 0.3.0 e o seu jogo na 0.2.1: o seu jogo está ATRASADO — atualize o jogo."
					  && fa.Titulo.Contains("VERSÃO DIFERENTE") && fa.Resumo.Contains("0.2.1") && fa.Resumo.Contains("0.3.0") && fa.Resumo.Contains("ATRASADO"),
					  fAtras?.Frase ?? "(nulo)");
			AfirmarVs("A FRASE DO ADIANTADO diz as duas versoes, a direcao e de quem e a vez",
					  fFrente is { } ff && ff.Frase == "Este servidor está na versão 0.2.1 e o seu jogo na 0.3.0: o seu jogo está ADIANTADO — o servidor ainda não foi atualizado."
					  && ff.Resumo.Contains("ADIANTADO"), fFrente?.Frase ?? "(nulo)");
			AfirmarVs("A FRASE DO OUTRO BUILD diz \"mesma versao, mas builds diferentes (o protocolo mudou)\" e mostra as duas digitais",
					  fBuild is { } fb && fb.Frase.Contains("mesma versão (0.2.1), mas são builds diferentes (o protocolo mudou)")
					  && fb.Frase.Contains("bbbbbbbb") && fb.Frase.Contains("aaaaaaaa") && fb.Titulo.Contains("BUILD DIFERENTE"), fBuild?.Frase ?? "(nulo)");
			Versao.Aviso semVersao = Versao.AvisoDeServidorSemVersao(jogoVelho);
			AfirmarVs("A FRASE DO SERVIDOR ANTIGO diz que ele nao informou a versao, e a do jogo",
					  semVersao.Frase.Contains("não informou a versão") && semVersao.Frase.Contains("servidor antigo") && semVersao.Frase.Contains("0.2.1")
					  && semVersao.Resumo.Contains("servidor sem versão (antigo)"), semVersao.Frase);
			AfirmarVs("sem desencontro nao ha aviso nenhum", Versao.AvisoDoDesencontro(daqui, daqui) == null);

			(string doProjeto, bool bate) = VersaoDoProjeto(Versao.Texto);
			AfirmarVs("A COPIA DO EXPORT BATE: o `config/version` do project.godot e a versao do Core/Versao.cs",
					  bate && Versao.TentarLer(doProjeto, out int doExport) && doExport == Versao.Numero,
					  $"project.godot \"{doProjeto}\" | Core/Versao.cs \"{Versao.Texto}\"");
			AfirmarVs("(contra-exemplo: o Core numa versao que o project.godot nao tem) a mesma checagem reprova a copia divergente",
					  !VersaoDoProjeto(Versao.TextoDe(Versao.Numero + 1)).Bate);

			// ---------------------------------------------------------------- 2. a impressao digital
			GD.Print("[versao] -- 2) a impressao digital do protocolo --");
			AfirmarVs("o FNV-1a de 64 bits bate com os vetores publicados (\"\", \"a\", \"foobar\"): a conta e a mesma em toda maquina",
					  Versao.Fnv1a64("") == 0xcbf29ce484222325UL && Versao.Fnv1a64("a") == 0xaf63dc4c8601ec8cUL
					  && Versao.Fnv1a64("foobar") == 0x85944171f73967e8UL);
			string desc = FioDaVersao.DescricaoDoProtocolo();
			string[] opcodesFora =
			[
				.. Enum.GetValues<Protocol.C2S>().Where(o => !desc.Contains($"{o}={(byte)o},")).Select(o => "C2S." + o),
				.. Enum.GetValues<Protocol.S2C>().Where(o => !desc.Contains($"{o}={(byte)o},")).Select(o => "S2C." + o),
			];
			AfirmarVs($"a descricao leva TODOS os {Enum.GetValues<Protocol.C2S>().Length} opcodes C2S e os {Enum.GetValues<Protocol.S2C>().Length} S2C, pelo nome e pelo valor",
					  opcodesFora.Length == 0 && desc.Contains("enum Jandirus.Net.Protocol+C2S:Byte{") && desc.Contains("enum Jandirus.Net.Protocol+S2C:Byte{"),
					  string.Join(",", opcodesFora));
			AfirmarVs("...as constantes de layout (o passo do servidor, os bits do input, o teto do argumento de verb)",
					  desc.Contains($"const Jandirus.Net.Protocol.TickHz={Protocol.TickHz}\n")
					  && desc.Contains($"const Jandirus.Net.Protocol.InputCorrendo={Protocol.InputCorrendo}\n")
					  && desc.Contains($"const Jandirus.Net.Protocol.MaxArgDeVerbo={Protocol.MaxArgDeVerbo}\n"));
			AfirmarVs("...e os campos dos estados que viajam (a ficha, o corpo no snapshot, o slot da selecao)",
					  desc.Contains("campo Jandirus.Net.SheetState.BP:System.Double\n")
					  && desc.Contains("campo Jandirus.Net.EntityState.Id:System.Int32\n")
					  && desc.Contains("campo Jandirus.Net.SlotInfo.Nome:System.String\n"));
			AfirmarVs("nada do que o compilador inventa (fecho de lambda, iterador) e nada com a versao do runtime entra na conta",
					  !desc.Contains("DisplayClass") && !desc.Contains("<>c") && !desc.Contains("Version=") && !desc.Contains("PublicKeyToken"));
			ulong digital = FioDaVersao.DigitalDoProtocolo;
			AfirmarVs("a digital e a conta da descricao, e sai igual duas vezes (nada de sorteio por processo)",
					  digital == Versao.Fnv1a64(desc) && digital == Versao.Fnv1a64(FioDaVersao.DescricaoDoProtocolo()) && digital != 0,
					  $"{digital:x16}  ({desc.Length} caracteres de contrato)");
			string comOpcodeNovo = desc.Replace("enum Jandirus.Net.Protocol+S2C:Byte{", "enum Jandirus.Net.Protocol+S2C:Byte{AlgoNovo=250,");
			string comOpcodeRenumerado = desc.Replace($"Versao={(byte)Protocol.S2C.Versao},", "Versao=98,");
			string comCampoNovo = desc + "campo Jandirus.Net.SheetState.CampoNovo:System.Int32\n";
			string comConstanteMudada = desc.Replace($"const Jandirus.Net.Protocol.TickHz={Protocol.TickHz}\n", "const Jandirus.Net.Protocol.TickHz=60\n");
			AfirmarVs("**A DIGITAL PEGA PROTOCOLO DIFERENTE**: opcode novo, opcode renumerado, campo novo num estado e constante mudada dao quatro digitais diferentes da daqui",
					  new[] { comOpcodeNovo, comOpcodeRenumerado, comCampoNovo, comConstanteMudada }.All(d => d != desc)
					  && new[] { digital, Versao.Fnv1a64(comOpcodeNovo), Versao.Fnv1a64(comOpcodeRenumerado), Versao.Fnv1a64(comCampoNovo), Versao.Fnv1a64(comConstanteMudada) }
						 .Distinct().Count() == 5);
			AfirmarVs("a identidade deste processo e a versao do Core + essa digital (sem forja na linha de comando)",
					  FioDaVersao.Forjada || FioDaVersao.DoProcesso == new IdentidadeDeBuild(Versao.Numero, digital),
					  FioDaVersao.Forjada ? "processo FORJADO pela linha de comando: nao medido" : "");

			// ---------------------------------------------------------------- 3. o fio
			GD.Print("[versao] -- 3) o fio: o bloco, os cortes, o lixo e o juizo de cada pedido --");
			var wBloco = new NetDataWriter();
			FioDaVersao.EscreverBloco(wBloco, daqui);
			byte[] bloco = wBloco.CopyData();
			var (lBloco, idBloco, sobraBloco) = LerBlocoDeTeste(bloco);
			AfirmarVs($"o bloco tem {FioDaVersao.BytesDoBloco} bytes, comeca por \"JVER\" e volta inteiro: nao sobra nem falta byte",
					  bloco.Length == FioDaVersao.BytesDoBloco && bloco[0] == (byte)'J' && bloco[1] == (byte)'V' && bloco[2] == (byte)'E' && bloco[3] == (byte)'R'
					  && lBloco == FioDaVersao.Leitura.Lida && idBloco == daqui && sobraBloco == 0, BitConverter.ToString(bloco));
			bool cortesCertos = LerBlocoDeTeste([]).Leitura == FioDaVersao.Leitura.Ausente
								&& FioDaVersao.LerBloco(null, out _) == FioDaVersao.Leitura.Ausente;
			for (int n = 1; n < bloco.Length; n++)
			{
				var (l, _, sobra) = LerBlocoDeTeste(bloco[..n]);
				cortesCertos &= l == FioDaVersao.Leitura.Ilegivel && sobra >= 0;
			}
			AfirmarVs("TODO CORTE do bloco e ilegivel (1 a 15 bytes), vazio e nulo sao \"ausente\" -- e ninguem le alem do fim", cortesCertos);
			byte[] semMarca = [.. bloco]; semMarca[0] ^= 0xFF;
			var wNeg = new NetDataWriter(); wNeg.Put(FioDaVersao.Marca); wNeg.Put(-5); wNeg.Put(1UL);
			var wAlto = new NetDataWriter(); wAlto.Put(FioDaVersao.Marca); wAlto.Put(Versao.NumeroMaximo + 1); wAlto.Put(1UL);
			AfirmarVs("bloco sem a marca, com numero negativo ou acima do teto: ilegivel",
					  LerBlocoDeTeste(semMarca).Leitura == FioDaVersao.Leitura.Ilegivel
					  && LerBlocoDeTeste(wNeg.CopyData()).Leitura == FioDaVersao.Leitura.Ilegivel
					  && LerBlocoDeTeste(wAlto.CopyData()).Leitura == FioDaVersao.Leitura.Ilegivel);
			var (lSobra, idSobra, sobrou) = LerBlocoDeTeste([.. bloco, 9, 9, 9]);
			AfirmarVs("bytes A MAIS depois do bloco nao atrapalham (campo novo se acrescenta no fim): le o bloco e deixa o resto",
					  lSobra == FioDaVersao.Leitura.Lida && idSobra == daqui && sobrou == 3);

			FioDaVersao.JuizoDoPedido Juizo(NetDataWriter w, out IdentidadeDeBuild c)
			{
				var r = new NetDataReader(w.Data, 0, w.Length);
				return FioDaVersao.JulgarPedido(r, daqui, out c);
			}
			AfirmarVs("O JUIZO: igual entra; atras, na frente e outro build sao recusados, cada um com o seu nome",
					  Juizo(FioDaVersao.PedidoDeConexao(daqui), out IdentidadeDeBuild cIgual) == FioDaVersao.JuizoDoPedido.Aceito && cIgual == daqui
					  && Juizo(FioDaVersao.PedidoDeConexao(atras), out IdentidadeDeBuild cAtras) == FioDaVersao.JuizoDoPedido.ClienteAtrasado && cAtras == atras
					  && Juizo(FioDaVersao.PedidoDeConexao(naFrente), out _) == FioDaVersao.JuizoDoPedido.ClienteAdiantado
					  && Juizo(FioDaVersao.PedidoDeConexao(outroBuild), out _) == FioDaVersao.JuizoDoPedido.OutroBuild);
			AfirmarVs("O JUIZO: so a senha = cliente SEM VERSAO (o antigo); senha errada, pedido vazio e nulo = nao e deste jogo",
					  Juizo(FioDaVersao.PedidoDeConexaoAntigo(), out _) == FioDaVersao.JuizoDoPedido.SemVersao
					  && Juizo(PedidoDeTeste("outra"), out _) == FioDaVersao.JuizoDoPedido.SenhaErrada
					  && Juizo(PedidoDeTeste("outra", daqui), out _) == FioDaVersao.JuizoDoPedido.SenhaErrada
					  && Juizo(PedidoDeTeste(null), out _) == FioDaVersao.JuizoDoPedido.SenhaErrada
					  && FioDaVersao.JulgarPedido(null, daqui, out _) == FioDaVersao.JuizoDoPedido.SenhaErrada);

			byte[] pedidoBom = FioDaVersao.PedidoDeConexao(daqui).CopyData();
			int tamDaSenha = FioDaVersao.PedidoDeConexaoAntigo().Length;
			var erradosNoCorte = new List<string>();
			for (int n = 0; n < pedidoBom.Length; n++)
			{
				FioDaVersao.JuizoDoPedido j = FioDaVersao.JulgarPedido(new NetDataReader(pedidoBom, 0, n), daqui, out _);
				FioDaVersao.JuizoDoPedido esperado = n < tamDaSenha ? FioDaVersao.JuizoDoPedido.SenhaErrada
												   : n == tamDaSenha ? FioDaVersao.JuizoDoPedido.SemVersao : FioDaVersao.JuizoDoPedido.VersaoIlegivel;
				if (j != esperado) erradosNoCorte.Add($"{n} bytes -> {j} (esperado {esperado})");
			}
			AfirmarVs($"TODO CORTE de um pedido bom ({pedidoBom.Length} bytes) e recusado pelo motivo certo: dentro da senha, so a senha, versao pela metade",
					  erradosNoCorte.Count == 0, string.Join("; ", erradosNoCorte.Take(4)));

			var sorte = new Random(20261011);
			int aceitosNoLixo = 0, estouros = 0, lidosNoLixo = 0;
			for (int i = 0; i < 4000; i++)
			{
				byte[] lixo = new byte[sorte.Next(0, 72)];
				sorte.NextBytes(lixo);
				// METADE DO LIXO COMECA PELA SENHA CERTA: e o caminho que chega ate o leitor do bloco.
				byte[] pedido = i % 2 == 0 ? lixo : [.. FioDaVersao.PedidoDeConexaoAntigo().CopyData(), .. lixo];
				try
				{
					if (FioDaVersao.JulgarPedido(new NetDataReader(pedido, 0, pedido.Length), daqui, out _) == FioDaVersao.JuizoDoPedido.Aceito) aceitosNoLixo++;
					if (LerBlocoDeTeste(lixo).Leitura == FioDaVersao.Leitura.Lida) lidosNoLixo++;
				}
				catch (Exception) { estouros++; }
			}
			AfirmarVs("4000 PEDIDOS DE LIXO (metade com a senha certa na frente): nenhuma excecao, nenhum aceito, nenhum bloco \"lido\"",
					  estouros == 0 && aceitosNoLixo == 0 && lidosNoLixo == 0, $"excecoes {estouros}, aceitos {aceitosNoLixo}, lidos {lidosNoLixo}");

			// ---------------------------------------------------------------- 4. a porta, pelo fio
			GD.Print($"[versao] -- 4) a porta de producao, pelo fio (UDP em 127.0.0.1:{porta}) --");
			int recusasAntes = RecusasPorVersao;

			DiscagemDeTeste igual = DiscarDeBancada(porta, FioDaVersao.PedidoDeConexao(daqui));
			bool primeiroEhVersao = igual.PrimeiroPacote is { Length: > 0 } pp && pp[0] == (byte)Protocol.S2C.Versao;
			var (lPrim, idPrim, sobraPrim) = igual.PrimeiroPacote is { Length: > 1 } p1 ? LerBlocoDeTeste(p1, 1) : (FioDaVersao.Leitura.Ausente, default(IdentidadeDeBuild), -1);
			AfirmarVs("**VERSAO IGUAL ENTRA**: a porta aceita o cliente de hoje", igual.Aceito && !igual.Recusado, $"{igual.Fim}, {igual.Ms} ms");
			AfirmarVs("...e o PRIMEIRO pacote que ele recebe e o `S2C.Versao`, com a versao e a digital DESTE servidor, sem sobrar nem faltar byte",
					  primeiroEhVersao && lPrim == FioDaVersao.Leitura.Lida && idPrim == daqui && sobraPrim == 0,
					  igual.PrimeiroPacote == null ? "nenhum pacote" : BitConverter.ToString(igual.PrimeiroPacote));
			AfirmarVs("...e o juizo anotado foi \"aceito\", sem contar recusa",
					  UltimoPedidoDeConexao is { Juizo: FioDaVersao.JuizoDoPedido.Aceito } && RecusasPorVersao == recusasAntes);

			// Atras, na frente, outro build: recusados, com a versao do servidor na carga -- e a frase que o cliente tira dela.
			foreach ((string nome, IdentidadeDeBuild cliente, FioDaVersao.JuizoDoPedido esperado, string direcao, string noLog) in new[]
			{
				("ATRAS", atras, FioDaVersao.JuizoDoPedido.ClienteAtrasado, "ATRASADO — atualize o jogo", "ATRAS deste servidor"),
				("NA FRENTE", naFrente, FioDaVersao.JuizoDoPedido.ClienteAdiantado, "ADIANTADO — o servidor ainda não foi atualizado", "NA FRENTE deste servidor"),
				("NA MESMA VERSAO COM OUTRO PROTOCOLO", outroBuild, FioDaVersao.JuizoDoPedido.OutroBuild, "builds diferentes (o protocolo mudou)", "outro protocolo"),
			})
			{
				int antes = RecusasPorVersao;
				DiscagemDeTeste d = DiscarDeBancada(porta, FioDaVersao.PedidoDeConexao(cliente));
				var (l, doServidor, sobra) = LerBlocoDeTeste(d.Carga);
				Versao.Aviso? aviso = l == FioDaVersao.Leitura.Lida ? Versao.AvisoDoDesencontro(cliente, doServidor) : null;
				AfirmarVs($"**CLIENTE {nome} E RECUSADO** antes de virar conexao, e a carga da recusa e a versao DO SERVIDOR (inteira, sem sobra)",
						  d.Recusado && !d.Aceito && d.PrimeiroPacote == null && l == FioDaVersao.Leitura.Lida && doServidor == daqui && sobra == 0,
						  $"{d.Fim}, {d.Ms} ms, carga {BitConverter.ToString(d.Carga)}");
				AfirmarVs($"...com ela o cliente escreve a frase certa: \"{direcao}\", com as duas versoes",
						  aviso is { } av && av.Frase.Contains(direcao) && av.Frase.Contains(daqui.Texto) && av.Frase.Contains(cliente.Texto),
						  aviso?.Frase ?? "(sem aviso)");
				AfirmarVs($"...e o log do servidor diz quem tem de atualizar (\"{noLog}\")",
						  UltimoPedidoDeConexao is { } u && u.Juizo == esperado && u.Cliente == cliente && u.Linha.Contains(noLog) && u.Linha.Contains("RECUSEI")
						  && RecusasPorVersao == antes + 1, UltimoPedidoDeConexao?.Linha ?? "");
			}

			// O cliente antigo: so a senha.
			{
				int antes = RecusasPorVersao;
				DiscagemDeTeste d = DiscarDeBancada(porta, FioDaVersao.PedidoDeConexaoAntigo());
				AfirmarVs("**CLIENTE SEM VERSAO (o antigo, so a senha) E RECUSADO LIMPO**: sem excecao, sem virar conexao",
						  d.Recusado && !d.Aceito && d.PrimeiroPacote == null && RecusasPorVersao == antes + 1, $"{d.Fim}, {d.Ms} ms");
				AfirmarVs("...a carga leva a versao do servidor do mesmo jeito (ele nao sabe ler: mostra o aviso generico dele)",
						  LerBlocoDeTeste(d.Carga) is { Leitura: FioDaVersao.Leitura.Lida } lido && lido.Id == daqui);
				AfirmarVs("...e o log do servidor e claro: \"cliente SEM VERSAO (um build antigo...)\", com a versao daqui",
						  UltimoPedidoDeConexao is { Juizo: FioDaVersao.JuizoDoPedido.SemVersao } u && u.Linha.Contains("SEM VERSAO")
						  && u.Linha.Contains("build antigo") && u.Linha.Contains(daqui.Texto), UltimoPedidoDeConexao?.Linha ?? "");
			}

			// A senha errada: a recusa seca de sempre.
			{
				int antes = RecusasPorVersao;
				DiscagemDeTeste d = DiscarDeBancada(porta, PedidoDeTeste("senha-errada", daqui));
				AfirmarVs("SENHA DE ENTRADA ERRADA: recusa seca (sem carga), e nao conta como recusa por versao",
						  d.Recusado && d.Carga.Length == 0 && RecusasPorVersao == antes
						  && UltimoPedidoDeConexao is { Juizo: FioDaVersao.JuizoDoPedido.SenhaErrada }, $"{d.Fim}, carga {d.Carga.Length} byte(s)");
			}

			// Lixo, pelo fio.
			{
				(string Nome, NetDataWriter Pedido)[] lixos =
				[
					("pedido vazio", PedidoDeTeste(null)),
					("um byte", PedidoDeTeste(null, cru: [7])),
					("tamanho de string absurdo", PedidoDeTeste(null, cru: [0xFF, 0xFF, 1, 2, 3])),
					("40 bytes quaisquer", PedidoDeTeste(null, cru: [.. Enumerable.Range(0, 40).Select(i => (byte)(i * 37 + 11))])),
					("senha certa + versao pela metade", PedidoDeTeste(Protocol.ConnectionKey, cru: bloco[..9])),
					("senha certa + 16 bytes sem a marca", PedidoDeTeste(Protocol.ConnectionKey, cru: semMarca)),
				];
				var ruins = new List<string>();
				foreach ((string nome, NetDataWriter pedido) in lixos)
				{
					DiscagemDeTeste d = DiscarDeBancada(porta, pedido);
					if (!d.Recusado || d.Aceito) ruins.Add($"{nome}: {d.Fim}");
				}
				AfirmarVs($"**LIXO NAO DERRUBA A PORTA**: {lixos.Length} pedidos tortos pelo fio (vazio, um byte, string absurda, bytes quaisquer, versao cortada, sem a marca), todos recusados",
						  ruins.Count == 0, string.Join("; ", ruins));
				DiscagemDeTeste depois = DiscarDeBancada(porta, FioDaVersao.PedidoDeConexao(daqui));
				AfirmarVs("...e a porta continua de pe: o cliente de hoje entra logo depois do lixo", depois.Aceito, depois.Fim);
			}

			// A identidade de bancada do servidor: o MESMO pedido muda de lado quando o servidor muda de versao.
			VersaoDeTeste = naFrente;
			{
				DiscagemDeTeste d = DiscarDeBancada(porta, FioDaVersao.PedidoDeConexao(daqui));
				var (l, doServidor, _) = LerBlocoDeTeste(d.Carga);
				AfirmarVs("o servidor uma versao NA FRENTE recusa o cliente de hoje, e a carga diz a versao NOVA do servidor",
						  d.Recusado && l == FioDaVersao.Leitura.Lida && doServidor == naFrente
						  && Versao.AvisoDoDesencontro(daqui, doServidor) is { } av && av.Frase.Contains("ATRASADO"), d.Fim);
			}
			VersaoDeTeste = versaoAntes;

			// O log nao e afogado por quem insiste.
			{
				_recusasNoLog.Clear();   // caderno em branco: a primeira das tres escreve, as outras duas nao
				int linhasAntes = RecusasNoLog, recusas = RecusasPorVersao;
				DiscarDeBancada(porta, FioDaVersao.PedidoDeConexao(atras));
				DiscarDeBancada(porta, FioDaVersao.PedidoDeConexao(atras));
				DiscarDeBancada(porta, FioDaVersao.PedidoDeConexao(atras));
				AfirmarVs("QUEM INSISTE NAO AFOGA O LOG: a mesma recusa do mesmo endereco, tres vezes seguidas, e recusada nas tres e escreve UMA linha",
						  RecusasPorVersao == recusas + 3 && RecusasNoLog == linhasAntes + 1,
						  $"recusas +{RecusasPorVersao - recusas}, linhas no log +{RecusasNoLog - linhasAntes}");
				DiscarDeBancada(porta, FioDaVersao.PedidoDeConexao(naFrente));
				AfirmarVs("...e outro MOTIVO do mesmo endereco escreve a linha dele (o silencio e por endereco + motivo)",
						  RecusasNoLog == linhasAntes + 2, $"linhas no log +{RecusasNoLog - linhasAntes}");

				// A ENXURRADA: enderecos diferentes furam o silencio por endereco, e quem segura e o teto de linhas por
				// minuto. O fio de bancada so sabe discar de 127.0.0.1, entao aqui quem fala e o proprio anotador de
				// producao, com um endereco de documentacao (203.0.113.x) por recusa.
				_minutoDasRecusas = System.Environment.TickCount64;
				_linhasNoMinuto = _caladasNoMinuto = 0;
				int linhas0 = RecusasNoLog, enxurrada = TetoDeLinhasDeRecusaPorMinuto + 4;
				for (int i = 0; i < enxurrada; i++)
				{
					string de = "203.0.113." + i;
					AnotarRecusa(de, FioDaVersao.JuizoDoPedido.ClienteAtrasado, LinhaDoJuizo(FioDaVersao.JuizoDoPedido.ClienteAtrasado, de, atras, daqui));
				}
				AfirmarVs($"A ENXURRADA NAO AFOGA O LOG: {enxurrada} recusas de {enxurrada} enderecos diferentes no mesmo minuto escrevem so {TetoDeLinhasDeRecusaPorMinuto} linhas, e as outras 4 ficam contadas pro resumo",
						  RecusasNoLog == linhas0 + TetoDeLinhasDeRecusaPorMinuto && _caladasNoMinuto == 4,
						  $"linhas +{RecusasNoLog - linhas0}, caladas {_caladasNoMinuto}");
				_minutoDasRecusas = 0;   // o resto da bancada volta a ter o minuto dele
				_linhasNoMinuto = _caladasNoMinuto = 0;
				_recusasNoLog.Clear();
			}

			// ---------------------------------------------------------------- os defeitos injetados da porta
			GD.Print("[versao] -- os defeitos injetados da porta --");
			AceitaQualquerVersaoDeTeste = true;
			try
			{
				DiscagemDeTeste d = DiscarDeBancada(porta, FioDaVersao.PedidoDeConexao(atras));
				var (l, doServidor, _) = d.PrimeiroPacote is { Length: > 1 } pk ? LerBlocoDeTeste(pk, 1) : (FioDaVersao.Leitura.Ausente, default(IdentidadeDeBuild), 0);
				AfirmarVs("(defeito injetado: o servidor nao compara a versao) o cliente ATRAS e ACEITO -- a regua da porta sabe reprovar",
						  d.Aceito && !d.Recusado, d.Fim);
				AfirmarVs("...e e ai que a checagem DO CLIENTE pega: o primeiro pacote ainda diz a versao do servidor, e ela nao bate com a dele",
						  l == FioDaVersao.Leitura.Lida && doServidor == daqui && Versao.AvisoDoDesencontro(atras, doServidor) is { } av && av.Frase.Contains("ATRASADO"));
			}
			finally { AceitaQualquerVersaoDeTeste = false; }

			foreach ((string nome, byte[] cargaForjada) in new (string, byte[])[]
			{
				("vazia", []), ("truncada (9 dos 16 bytes)", bloco[..9]), ("de lixo (16 bytes sem a marca)", semMarca), ("de um byte", [0x4A]),
			})
			{
				CargaDaRecusaDeTeste = cargaForjada;
				try
				{
					DiscagemDeTeste d = DiscarDeBancada(porta, FioDaVersao.PedidoDeConexao(atras));
					FioDaVersao.Leitura l = LerBlocoDeTeste(d.Carga).Leitura;
					AfirmarVs($"(defeito injetado: recusa com carga {nome}) o cliente e recusado, o leitor nao lanca, e NAO ha frase de versao -- sobra o aviso generico",
							  d.Recusado && d.Carga.Length == cargaForjada.Length && l != FioDaVersao.Leitura.Lida, $"{l}, {d.Carga.Length} byte(s)");
				}
				finally { CargaDaRecusaDeTeste = null; }
			}

			PortaAntigaDeTeste = true;
			try
			{
				DiscagemDeTeste novo = DiscarDeBancada(porta, FioDaVersao.PedidoDeConexao(atras));
				DiscagemDeTeste antigo = DiscarDeBancada(porta, FioDaVersao.PedidoDeConexaoAntigo());
				AfirmarVs("(defeito injetado: a porta de ANTES, so a senha) o cliente de outra versao ENTRA, e nenhum pacote de versao chega -- o servidor antigo que o cliente novo tem de perceber sozinho",
						  novo.Aceito && novo.PrimeiroPacote == null && antigo.Aceito && antigo.PrimeiroPacote == null, $"{novo.Fim} | {antigo.Fim}");
			}
			finally { PortaAntigaDeTeste = false; }

			DiscagemDeTeste noFim = DiscarDeBancada(porta, FioDaVersao.PedidoDeConexao(atras));
			AfirmarVs("defeitos desligados: a porta volta a recusar o cliente atras, com a versao na carga",
					  noFim.Recusado && LerBlocoDeTeste(noFim.Carga) is { Leitura: FioDaVersao.Leitura.Lida } volta && volta.Id == daqui);
		}
		catch (Exception e)
		{
			AfirmarVs("a bancada rodou ate o fim", false, e.ToString());
		}
		finally
		{
			Versao.DigitalForaDaContaDeTeste = false;
			AceitaQualquerVersaoDeTeste = PortaAntigaDeTeste = false;
			CargaDaRecusaDeTeste = null;
			VersaoDeTeste = versaoAntes;
			// AS FLAGS DE BANCADA DA LINHA DE COMANDO VALEM DEPOIS DA BANCADA (ela as desligou pra medir a porta de verdade).
			string[] args = OS.GetCmdlineArgs();
			PortaAntigaDeTeste = Array.IndexOf(args, "--servidorantigo") >= 0;
			AceitaQualquerVersaoDeTeste = Array.IndexOf(args, "--servidoraceitatudo") >= 0;
			GD.Print($"[versao] ================ {_vsOk} OK, {_vsFalhou} FALHA(S) ================");
		}
	}
}
