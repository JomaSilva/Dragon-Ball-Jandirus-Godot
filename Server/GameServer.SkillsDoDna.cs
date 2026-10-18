using Godot;
using Jandirus.Core.Races;
using Jandirus.Core.Stats;

namespace Jandirus.Server;

/// <summary>
/// (2026-10-17) AS SKILLS DO DNA DO BIO-ANDROIDE -- o adendo do dono: *"o bio androide pode escolher dentre as raças de seu dna quais skills
/// (passivas e ativas) ele quer, com limite de 6 skills"*. A regra mora no Core (`Races.SkillsRaciais`); aqui, o aperto do botao.
///
///   * O BOTAO: um por skill que o sangue da e que ele ainda nao tem (`hab:dna:&lt;bit&gt;`, na aba Skills), enquanto houver vaga
///     (`Fatos.SkillDoDnaPorEscolher`). A escolha e PERMANENTE e uma por vez: o botao dela some, e com a sexta somem todos.
///   * O EFEITO: o bit entra em `Fighter.bio_skills`. O tique racial o pergunta no proximo tique (`SkillsDoCorpo`); a ficha lenta leva a
///     mascara pro cliente (`AtributosState.SkillsRaciais`), e a ativa escolhida ganha o botao dela; a IA, se o corpo for dela, a aperta.
/// </summary>
public sealed partial class GameServer
{
	/// <summary>O aperto de `dna:&lt;skill&gt;`: escolhe a skill, se o sangue a da e se ha vaga.</summary>
	private void EscolherSkillDoDna(ServerPlayer pl, string id)
	{
		Fighter f = pl.Ficha;
		if (SkillsRaciais.PorId(id) is not { } skill)
		{
			Avisar(pl, SangueDoBio.EhBio(f) ? "essa skill não vem de nenhuma raça do seu sangue." : SkillsRaciais.RecusaSemDna);
			return;
		}
		string nao = SkillsRaciais.PorQueNaoEscolhe(f, skill.Bit);
		if (nao.Length > 0) { Avisar(pl, nao); return; }

		f.bio_skills |= (uint)skill.Bit;
		pl.SigAtributos = "";   // a mascara viaja na ficha lenta: o botao da ativa aparece, o da escolha some
		Persistir(pl);
		int vagas = SkillsRaciais.AindaEscolhe(f);
		Avisar(pl, $"o seu corpo desperta {skill.Nome}" + (skill.EhAtiva ? " -- o botão dela está na aba Skills." : " -- ela já vale.")
					   + (vagas > 0 ? $" Ainda dá pra escolher {vagas}." : " O seu DNA está completo."));
		GD.Print($"[server] {pl.Name}: skill do DNA {skill.Nome} -- agora {SkillsRaciais.Nomes(SkillsRaciais.EscolhidasDoBio(f))}");
	}
}
