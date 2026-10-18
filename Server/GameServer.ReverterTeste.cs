using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Forms;
using Jandirus.Core.Stats;

namespace Jandirus.Server;

/// <summary>
/// A BANCADA DO NOCAUTE E DA TECLA DE REVERTER (`--reverterteste`) -- os itens 7 e 8 do playtest de 2026-10-10:
///
///   7. *"Nocaute reseta o ki: ao ser nocauteado, se o ki estiver acima de 100%, ele volta para 100%. Com 100%
///      ou menos, nada muda."*
///   8. *"Botao de reverter/liberar. Ki acima de 100%: libera o excedente e o ki volta para 100%; tem
///      prioridade, mesmo se estiver transformado. Ki em 100% ou menos e transformado: volta para o estagio
///      anterior da linha (ex.: SSJ2 -> SSJ); sem estagio anterior, volta para a base. Ki em 100% ou menos e na
///      forma base: nao faz nada."*
///
/// No PRIMEIRO LOGIN de um Saiyajin, e pelos funis de producao: o nocaute e o `CombatState.Nocautear` (o
/// funil unico), a tecla e a `ReverterOuLiberar` que o pacote da tecla X chama, e o tanque anda pelo
/// `TickDaCarga`. Quatro defeitos injetados -- o nocaute que guarda a sobrecarga, a tecla que nao libera, a
/// tecla que salta ao piso -- e o "voltar ao normal" da aba de formas conferido como o que NAO mudou.
///
///     Godot --headless --path . --host --rede 7995 --reverterteste --raca Saiyan --conta bancada_reverter --nome Recuo
/// </summary>
public partial class GameServer
{
	private bool _reverterDeTeste;

	private void RodarBancadaDoReverter(ServerPlayer pl)
	{
		GD.Print("\n[reverter] ================ O NOCAUTE E A TECLA DE REVERTER / LIBERAR (playtest, 2026-10-10) ================");
		int ok = 0, falhou = 0;
		void Checa(string nome, bool cond, string detalhe = "")
		{
			if (cond) { ok++; GD.Print($"[reverter]   OK    {nome}"); }
			else { falhou++; GD.PrintErr($"[reverter]   FALHA {nome}   [{detalhe}]"); }
		}

		Fighter f = pl.Ficha;
		EstadoDeForma est = pl.Forma;

		// ============================ O HOST VOLTA COMO ESTAVA ============================
		double kiDeAntes = f.Ki, vigorDeAntes = f.stamina;
		string formaDeAntes = est.Atual;
		HashSet<int> liberadasDeAntes = [.. est.Liberadas], estreiasDeAntes = [.. est.EstreiaVista];
		bool? gradesDeAntes = est.GradesLigados;

		string Forma() => est.Atual;
		double Razao() => f.Ki / Math.Max(f.MaxKi, 1e-9);

		// POE O CORPO NUMA FORMA, tendo ESTADO em cada degrau do caminho -- e o que "subiu a escada" deixa
		// escrito. A cena e zerada porque a bancada nao espera cinematica: o que se mede e o destino.
		void Subir(params string[] caminho)
		{
			est.Entrar(Catalogo.IdBase);
			foreach (string id in caminho) est.Entrar(id);
			AplicarForma(pl);
			pl.CenaSegundos = 0;
		}
		List<string> ApertarX()
		{
			pl.CenaSegundos = 0;
			return Ouvir(() => ReverterOuLiberar(pl));
		}

		try
		{
			Checa("(montagem) o host tem escada de formas (e um Saiyajin)", TemEscada(pl), pl.Race);

			// =====================================================================
			GD.Print("[reverter] -- 7) o nocaute dispersa a sobrecarga --");
			// =====================================================================
			Subir();
			f.Ki = f.MaxKi * 1.3;
			pl.Combate.Nocautear(12);
			Checa("**NOCAUTEADO ACIMA DE 100%, O KI VOLTA PRA 100%**", f.KO && Math.Abs(Razao() - 1) < 1e-9, $"{Razao():P0}");

			double maior = 0;
			for (int i = 0; i < 120; i++) { TickDaCarga(pl, 0.1); maior = Math.Max(maior, Razao()); }
			Checa("...e os 12 s no chao passam sem o Ki subir de novo acima de 100% (o tique da carga, 120 vezes)",
				  maior <= 1 + 1e-9, $"pico {maior:P1}");
			pl.Combate.Levantar();

			f.Ki = f.MaxKi * 0.8;
			pl.Combate.Nocautear(12);
			Checa("COM 100% OU MENOS, NADA MUDA: quem cai a 80% continua a 80%", Math.Abs(Razao() - 0.8) < 1e-9, $"{Razao():P0}");
			pl.Combate.Levantar();

			CombatState.NocauteGuardaASobrecargaDeTeste = true;
			try
			{
				f.Ki = f.MaxKi * 1.3;
				pl.Combate.Nocautear(12);
				Checa("(defeito injetado: o nocaute nao mexe no Ki) quem cai a 130% continua a 130% -- o jogo de antes",
					  Math.Abs(Razao() - 1.3) < 1e-9, $"{Razao():P0}");
			}
			finally { CombatState.NocauteGuardaASobrecargaDeTeste = false; }
			pl.Combate.Levantar();
			f.Ki = f.MaxKi;

			// =====================================================================
			GD.Print("[reverter] -- 8) a tecla: primeiro libera, depois recua um estagio, e na base nao faz nada --");
			// =====================================================================
			Subir("ssj1", "ssj2");
			Checa("(montagem) o corpo esta em SSJ2, tendo passado pelo SSJ1", Forma() == "ssj2" && est.JaViuAEstreia("ssj1"), Forma());

			f.Ki = f.MaxKi * 1.3;
			List<string> falas = ApertarX();
			Checa("**KI ACIMA DE 100%: O TOQUE LIBERA O EXCEDENTE** e o Ki volta pra 100%", Math.Abs(Razao() - 1) < 1e-9, $"{Razao():P0}");
			Checa("...com PRIORIDADE: transformado, a forma nao se mexe neste toque", Forma() == "ssj2", Forma());
			Checa("...e o jogador e avisado", Disse(falas, "libera"), Ultimos(falas));

			falas = ApertarX();
			Checa("**KI EM 100%, TRANSFORMADO: VOLTA UM ESTAGIO** (SSJ2 -> SSJ)", Forma() == "ssj1", Forma());
			Checa("...e o Ki nao se mexe nesse toque", Math.Abs(Razao() - 1) < 1e-9, $"{Razao():P0}");
			Checa("...e ele diz pra onde recuou", Disse(falas, "recua"), Ultimos(falas));

			falas = ApertarX();
			Checa("PRIMEIRO ESTAGIO DA LINHA: o toque seguinte volta pra BASE", Forma() == Catalogo.IdBase, Forma());

			falas = ApertarX();
			Checa("**NA BASE, COM 100% OU MENOS: NADA** -- nem forma, nem Ki, nem aviso",
				  Forma() == Catalogo.IdBase && Math.Abs(Razao() - 1) < 1e-9 && falas.Count == 0, Ultimos(falas));

			f.Ki = f.MaxKi * 1.2;
			falas = ApertarX();
			Checa("na base e ACIMA de 100%: libera do mesmo jeito (a regra 1 nao olha a forma)",
				  Math.Abs(Razao() - 1) < 1e-9 && Forma() == Catalogo.IdBase && Disse(falas, "libera"), $"{Razao():P0}");

			// ---- os dois defeitos da tecla
			Subir("ssj1", "ssj2");
			f.Ki = f.MaxKi * 1.3;
			ReverterNaoLiberaDeTeste = true;
			try
			{
				ApertarX();
				Checa("(defeito injetado: a tecla nao libera) a 130% o toque DESCE A FORMA e o Ki continua acima de 100% -- o jogo de antes",
					  Forma() != "ssj2" && Razao() > 1.29, $"{Forma()}, {Razao():P0}");
			}
			finally { ReverterNaoLiberaDeTeste = false; }

			Subir("ssj1", "ssj2");
			f.Ki = f.MaxKi;
			ReverterVaiAoPisoDeTeste = true;
			try
			{
				ApertarX();
				Checa("(defeito injetado: a tecla salta ao piso) do SSJ2 o toque cai direto na BASE -- o jogo de antes",
					  Forma() == Catalogo.IdBase, Forma());
			}
			finally { ReverterVaiAoPisoDeTeste = false; }

			// =====================================================================
			GD.Print("[reverter] -- 8) qual e o \"estagio anterior\" --");
			// =====================================================================
			Subir("ssj1", "ssj2", "ssj3");
			ApertarX();
			Checa("SSJ3 -> SSJ2", Forma() == "ssj2", Forma());

			est.GradesLigados = true;
			Subir("ssj1", "grade2", "grade3");
			ApertarX();
			Checa("o GRADE e ramo lateral: do Grade 3 volta-se ao SSJ1 (o tronco), e nao ao Grade 2", Forma() == "ssj1", Forma());
			est.GradesLigados = gradesDeAntes;

			// RECUAR NAO ENSINA FORMA: o corpo chega ao SSJ4 pelo Oozaru Dourado (a forma e escrita direto,
			// como o `GameServer.Oozaru` faz) conhecendo so o SSJ1.
			est.Liberadas.Clear();
			est.EstreiaVista.Clear();
			Subir("ssj1");
			est.Liberar("ssj4");
			est.Atual = "ssj4";
			AplicarForma(pl);
			ApertarX();
			Checa("**RECUAR NAO ENSINA FORMA**: do SSJ4, quem so conhece o SSJ1 cai no SSJ1 -- sem passar pelo SSJ3 nem pelo SSJ2",
				  Forma() == "ssj1" && !est.Despertou("ssj3") && !est.Despertou("ssj2"),
				  $"{Forma()}; ssj3 liberado={est.Despertou("ssj3")}, ssj2 liberado={est.Despertou("ssj2")}");

			est.Liberadas.Clear();
			est.EstreiaVista.Clear();
			est.Entrar(Catalogo.IdBase);
			est.Liberar("ssj4");
			est.Atual = "ssj4";
			AplicarForma(pl);
			ApertarX();
			Checa("...e quem nao conhece degrau nenhum abaixo volta pra BASE", Forma() == Catalogo.IdBase, Forma());

			// =====================================================================
			GD.Print("[reverter] -- o que NAO mudou --");
			// =====================================================================
			Subir("ssj1", "ssj2", "ssj3");
			pl.CenaSegundos = 0;
			Verbo(pl, "forma", Catalogo.IdBase);
			Checa("escolher BASE na aba de formas continua indo direto ao normal (o pedido la e outro)", Forma() == Catalogo.IdBase, Forma());

			Subir("ssj1", "ssj2", "ssj3");
			pl.CenaSegundos = 0;
			Transformar(pl, subir: false);
			Checa("...e o `Transformar(subir: false)` da IA tambem (o `DescerForma` do cerebro)", Forma() == Catalogo.IdBase, Forma());
		}
		catch (Exception e)
		{
			Checa("a bancada rodou ate o fim", false, e.ToString());
		}
		finally
		{
			CombatState.NocauteGuardaASobrecargaDeTeste = false;
			ReverterNaoLiberaDeTeste = false;
			ReverterVaiAoPisoDeTeste = false;
			if (f.KO) pl.Combate.Levantar();
			est.GradesLigados = gradesDeAntes;
			est.Liberadas.Clear();
			foreach (int r in liberadasDeAntes) est.Liberadas.Add(r);
			est.EstreiaVista.Clear();
			foreach (int r in estreiasDeAntes) est.EstreiaVista.Add(r);
			est.Atual = formaDeAntes;
			AplicarForma(pl);
			pl.CenaSegundos = 0;
			f.Ki = kiDeAntes;
			f.stamina = vigorDeAntes;
		}

		GD.Print($"[reverter] ================ {ok} OK, {falhou} FALHA(S) ================");
	}
}
