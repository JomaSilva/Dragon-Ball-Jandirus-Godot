using Godot;
using Jandirus.Net;

namespace Jandirus.Client;

/// <summary>
/// A familia de provas das abas Sense/Scan da `--diagabas`. Ver o cabecalho de `RoboDasAbas.cs`.
///
/// O QUE ELA AFIRMA, nas duas metades: SEM `Poder.Sense` a aba NAO esta na barra (o contrato do `Abas()`);
/// COM a skill -- dada pelo verb de admin `dar skill`, que e o `Give (Skill)` do original, porque a bancada
/// entra como host -- a aba aparece sem reabrir o menu, o pacote `S2C.Sentidos` chega pelo tique de 1 Hz,
/// o fio NAO traz BP absoluto (todo `Bp` e NaN: o sigilo), a aba desenha um cartao por presenca (ou a
/// frase do DM quando nao ha nenhuma) e nenhuma linha "Battle Power" existe. Depois a skill e tirada e
/// a aba SOME, com o menu caindo em Stats (o desvio "a aba aberta DEIXOU de existir" do `Redesenhar`).
/// Contra-exemplo injetado: uma presenca com BP no pacote de Sense reprova a prova de sigilo.
/// </summary>
public partial class RoboDasAbas
{
	private async System.Threading.Tasks.Task F6_Sentidos(MenuJogo menu, GameClient cli)
	{
		Nota("--- F6: Sentidos: sem a skill a aba nao existe; com ela a leitura chega e o fio nao traz BP absoluto ---");

		// ---- 1) sem o sentido, sem a aba ----
		bool temSense = cli.Atributos.Tem(Protocol.Poder.Sense), temScouter = cli.Atributos.Tem(Protocol.Poder.Scouter);
		string[] abas = menu.AbasDeTeste;
		Checa("um personagem recem-nascido nao tem Poder.Sense nem scouter", !temSense && !temScouter, $"sense={temSense} scouter={temScouter}");
		Checa("...e por isso NAO ha aba Sense nem Scan na barra (o contrato do Abas())", !abas.Contains("Sense") && !abas.Contains("Scan"), string.Join(",", abas));
		Checa("...nem pagina montada pra elas", menu.PaginaDeTeste("Sense") == null && menu.PaginaDeTeste("Scan") == null);

		if (!cli.Atributos.Tem(Protocol.Poder.Admin))
		{
			Nota("  PULADA  a metade COM a skill precisa do verb de admin 'dar skill' (a bancada nao entrou como admin)");
			_pulados++;
			_naoMedidos.Add("Sentidos com a skill (precisa de admin)");
			return;
		}

		// ---- 2) a skill chega por admin, e a aba nasce ----
		int pacotes = 0;
		void Contar() => pacotes++;
		cli.SentidosMudaram += Contar;
		try
		{
			cli.SendVerbo("admin_skill_dar", "|Sense");   // alvo vazio = eu mesmo; por NOME, como o painel de admin manda
			bool acendeu = await Ate(() => cli.Atributos.Tem(Protocol.Poder.Sense), 8);
			Checa("o verb de admin 'dar skill' (Sense) acende Poder.Sense na ficha lenta", acendeu);
			if (!acendeu) return;
			bool naBarra = await Ate(() => menu.AbasDeTeste.Contains("Sense"), 3);
			Checa("...e a aba Sense APARECE na barra sem reabrir o menu (a barra se refaz quando a lista de abas muda)", naBarra, string.Join(",", menu.AbasDeTeste));
			Button? b = Botao(menu, "Sense");
			Checa("a aba Sense tem botao", b != null);
			if (b == null) return;
			await Clicar(b);
			await Quadros(4);
			Checa("clicar nele abre 'Sense'", menu.AbaDeTeste == "Sense", menu.AbaDeTeste);

			// ---- 3) o pacote, e o sigilo dentro dele ----
			bool chegou = await Ate(() => pacotes > 0, 4);
			Checa("o pacote S2C.Sentidos chega em ate 4 s depois da skill (o tique de 1 Hz do servidor)", chegou, $"{pacotes} pacotes");
			await Quadros(3);   // a aba remonta pela assinatura na proxima ficha (5 Hz)
			List<Protocol.PresencaState> lista = cli.Sentidos;
			Checa("no modo Sense o fio NAO traz BP absoluto: todo `Bp` e NaN (o sigilo do GameServer.Sigilo)",
				  !cli.SentidosSaoDoScouter && SemNumeroAbsoluto(lista), $"{lista.Count} presencas");
			Injeta("uma presenca com Bp = 123 injetada na lista reprova a prova de sigilo",
				   !SemNumeroAbsoluto([.. lista, new Protocol.PresencaState { Nome = "intruso", Bp = 123 }]));

			// ---- 4) o que a aba desenhou ----
			Control? pg = menu.PaginaDeTeste("Sense");
			Checa("a pagina de Sense esta montada", pg != null);
			if (pg == null) return;
			string? faixa = menu.ValorDesenhado("Sense", "Presenças");
			Checa("a Faixa 'Presenças' escreve a contagem da lista recebida", faixa != null && faixa.StartsWith($"{lista.Count}   "), faixa ?? "(nula)");
			int cartoes = Todos(pg).OfType<PanelContainer>().Count(x => x.HasMeta("cartao") && x.GetMeta("titulo").AsString().Length == 0);
			Checa($"a aba desenha um cartao por presenca ({lista.Count}) -- ou a frase do DM quando nao ha nenhuma",
				  cartoes == lista.Count && (lista.Count > 0 || Rotulo(pg, "Você não sente nenhuma presença notável.") != null),
				  $"{cartoes} cartoes, {lista.Count} presencas");
			Checa("nenhuma linha 'Battle Power' na aba Sense (o numero so existe no Scan)", menu.ValorDesenhado("Sense", "Battle Power") == null);
			if (lista.Count > 0)
			{
				string? poder = menu.ValorDesenhado("Sense", "poder relativo");
				Checa("cada presenca traz 'poder relativo' em % inteiro, o numero da lista",
					  poder != null && poder == $"{lista[0].PoderRelativo:0}%", poder ?? "(nulo)");
				int pilulas = Todos(pg).OfType<PanelContainer>().Count(x => x.HasMeta("pilula") && x.GetMeta("pilula").AsString() is "perto" or "neste mundo" or "na galáxia");
				Checa("...e a pilula do alcance (perto / neste mundo / na galáxia)", pilulas == lista.Count, $"{pilulas}");
			}

			Image? foto = await Foto();
			await Guardar("sentidos-01-sense", foto);
			PanelContainer? cartao = Todos(pg).OfType<PanelContainer>().FirstOrDefault(x => x.HasMeta("cartao"));
			if (cartao != null)
			{
				(Color cor, float frac) = Moda(foto, Caixa(cartao.GetGlobalRect(), 4));
				ChecaNoPixel("o primeiro cartao da aba e pintado com a paleta do tema (moda de pixel)", foto != null, NaPaleta(cor), $"moda {Hex(cor)} ({frac * 100:0}%)");
			}

			// ---- 5) a skill vai embora, e a aba com ela ----
			cli.SendVerbo("admin_skill_tirar", "|Sense");
			bool apagou = await Ate(() => !cli.Atributos.Tem(Protocol.Poder.Sense), 8);
			Checa("tirar a skill apaga Poder.Sense", apagou);
			await Quadros(3);
			Checa("...a aba Sense saiu da barra e o menu caiu em Stats (a aba aberta DEIXOU de existir)",
				  !menu.AbasDeTeste.Contains("Sense") && menu.AbaDeTeste == "Stats", $"{string.Join(",", menu.AbasDeTeste)} / {menu.AbaDeTeste}");
		}
		finally
		{
			cli.SentidosMudaram -= Contar;
		}
	}

	/// <summary>A REGRA DO SIGILO no cliente, como funcao pura pra caber contra-exemplo: nenhum BP absoluto na lista.</summary>
	private static bool SemNumeroAbsoluto(List<Protocol.PresencaState> lista) => lista.All(p => double.IsNaN(p.Bp));
}
